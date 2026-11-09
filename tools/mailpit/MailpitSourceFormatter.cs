namespace CloudyWing.McpLab.Mailpit;

/// <summary>
/// Builds the <c>get_message_source</c> response from a raw message body that was read with a size limit.
/// </summary>
public static class MailpitSourceFormatter {
    /// <summary>
    /// Creates the tool response for a raw message source.
    /// </summary>
    /// <param name="id">The Mailpit message ID that was requested.</param>
    /// <param name="limit">The maximum number of characters to return.</param>
    /// <param name="body">The raw message body, which is truncated when it exceeds the response limit.</param>
    /// <param name="maxResponseBytes">The effective response limit.</param>
    /// <returns>The serialized tool response.</returns>
    public static string Format(string id, int limit, BoundedBody body, int maxResponseBytes) {
        string source = body.ToText();

        if (!body.Truncated) {
            return ToolResponse.Ok(new {
                id,
                limit,
                source_length = source.Length,
                truncated = source.Length > limit,
                source = LimitText(source, limit),
            });
        }

        // The full length is unknown in characters, so the byte size reported by the body is used.
        object data = new {
            id,
            limit,
            source_length = body.OriginalBytes,
            truncated = true,
            source = LimitText(source, limit),
        };

        return ToolResponse.Ok(
            ResponseTruncation.Append(data, body, maxResponseBytes),
            ResponseTruncation.Message(body.OriginalBytes, body.OriginalBytesExact, maxResponseBytes)
        );
    }

    private static string LimitText(string value, int limit) =>
        value.Length <= limit ? value : value[..limit];
}
