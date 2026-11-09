namespace CloudyWing.McpLab.Mosquitto;

/// <summary>
/// Collects received MQTT messages while keeping their payloads within a <see cref="ResponseBudget"/>.
/// Payloads are sliced as bytes and only the accepted prefix is decoded, so a large payload is never converted to a full string.
/// This type is thread-safe because MQTT message handlers run on a different thread than the tool method.
/// </summary>
public sealed class MqttPayloadCollector {
    private readonly object syncRoot = new();
    private readonly ResponseBudget budget;
    private readonly List<object> messages = [];
    private bool truncated;

    /// <summary>
    /// Initializes a new instance of <see cref="MqttPayloadCollector"/>.
    /// </summary>
    /// <param name="budget">The budget that limits the total payload bytes of all collected messages.</param>
    public MqttPayloadCollector(ResponseBudget budget) {
        ArgumentNullException.ThrowIfNull(budget);

        this.budget = budget;
    }

    /// <summary>
    /// Gets a snapshot of the collected messages in arrival order.
    /// A message whose payload was cut also carries <c>payload_original_bytes</c>.
    /// </summary>
    public IReadOnlyList<object> Messages {
        get {
            lock (syncRoot) {
                return [.. messages];
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether any payload was cut or any message was ignored because the budget ran out.
    /// </summary>
    public bool Truncated {
        get {
            lock (syncRoot) {
                return truncated;
            }
        }
    }

    /// <summary>
    /// Gets the payload bytes of all offered messages, including the bytes that were cut or ignored.
    /// </summary>
    public long ObservedBytes {
        get {
            lock (syncRoot) {
                return budget.ObservedBytes;
            }
        }
    }

    /// <summary>
    /// Offers a received message.
    /// </summary>
    /// <param name="topic">The topic the message was published to.</param>
    /// <param name="payload">The raw payload bytes.</param>
    /// <param name="qos">The quality of service level of the message.</param>
    /// <returns><see langword="true"/> when the message was recorded; <see langword="false"/> when it was ignored.</returns>
    public bool Add(string topic, ArraySegment<byte> payload, int qos) {
        lock (syncRoot) {
            // Once the budget has no room left, later messages are only counted.
            if (budget.Remaining <= 0) {
                budget.TakeUpTo(payload.Count);
                truncated = true;
                return false;
            }

            long accepted = budget.TakeUpTo(payload.Count);
            bool cut = accepted < payload.Count;
            string text = Utf8Text.DecodePrefix(payload.AsSpan(0, (int)accepted), cut);

            if (cut) {
                truncated = true;
                messages.Add(new {
                    topic,
                    payload = text,
                    qos,
                    payload_original_bytes = payload.Count,
                });
            } else {
                messages.Add(new {
                    topic,
                    payload = text,
                    qos,
                });
            }

            return true;
        }
    }

    /// <summary>
    /// Creates the array that a tool returns as response data.
    /// Without truncation the array equals <see cref="Messages"/>. With truncation, the last message also carries
    /// <c>truncated</c>, <c>original_bytes</c>, <c>original_bytes_exact</c> and <c>max_response_bytes</c>, so the top-level array shape is kept.
    /// </summary>
    /// <param name="maxResponseBytes">The effective response limit.</param>
    /// <remarks>
    /// The first message is always recorded because a budget has at least one byte, so a truncated collector is never empty.
    /// </remarks>
    public IReadOnlyList<object> CreateResponseData(int maxResponseBytes) {
        lock (syncRoot) {
            if (!truncated || messages.Count == 0) {
                return [.. messages];
            }

            object[] data = [.. messages];
            data[^1] = ResponseTruncation.Append(data[^1], budget.ObservedBytes, false, maxResponseBytes);
            return data;
        }
    }
}
