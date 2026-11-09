using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.Mosquitto;

/// <summary>
/// Provides MCP tools for publishing to and subscribing from MQTT brokers.
/// </summary>
[McpServerToolType]
public sealed class MqttTools {
    private static readonly string[] StatusInterestKeys = [
        "version", "uptime", "clients/connected",
        "messages/received", "messages/sent",
        "bytes/received", "bytes/sent",
    ];

    private readonly ConnectionRegistry registry;
    private readonly IDotEnvSnapshotProvider snapshotProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="MqttTools"/> with the specified connection registry.
    /// </summary>
    /// <param name="registry">The connection registry.</param>
    /// <param name="snapshotProvider">The .env snapshot provider used to read MCPLAB_MAX_RESPONSE_BYTES.</param>
    public MqttTools(ConnectionRegistry registry, IDotEnvSnapshotProvider snapshotProvider) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        this.registry = registry;
        this.snapshotProvider = snapshotProvider;
    }

    private int MaxResponseBytes => ServiceSettings.Common.MaxResponseBytes.Read(snapshotProvider.Current);

    /// <summary>
    /// 列出所有已設定的 MQTT 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 MQTT 連線")]
    public string ListConnections() =>
        ToolResponse.Ok(
            registry.All.Select(kv => new {
                name = kv.Key,
                environment = registry.EnvironmentOf(kv.Key),
                host = kv.Value.Host,
                port = kv.Value.Port,
                user = kv.Value.User,
            }));

    /// <summary>
    /// 測試 MQTT Broker 連線是否正常（訂閱測試 topic 以驗證 broker 實際處理協定）
    /// </summary>
    [McpServerTool, Description("測試 MQTT Broker 連線是否正常（訂閱測試 topic 以驗證 broker 實際處理協定）")]
    public async Task<string> PingConnection(
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            IMqttClient client = await registry.ConnectAsync(connection);

            try {
                string testTopic = $"mcp/ping/{Guid.NewGuid():N}";
                MqttClientSubscribeResult subResult = await client.SubscribeAsync(
                    new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f.WithTopic(testTopic))
                        .Build()
                );

                MqttClientSubscribeResultCode rc = subResult.Items.First().ResultCode;
                bool ok = rc is MqttClientSubscribeResultCode.GrantedQoS0
                    or MqttClientSubscribeResultCode.GrantedQoS1
                    or MqttClientSubscribeResultCode.GrantedQoS2;

                return ok
                    ? ToolResponse.Ok(new {
                        result = rc.ToString(),
                    }, "Broker acknowledged subscription.")
                    : ToolResponse.Error($"Broker denied subscription ({rc}).");
            } finally {
                await client.DisconnectAsync();
                client.Dispose();
            }
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 發布訊息到指定的 MQTT topic
    /// </summary>
    [McpServerTool, Description("發布訊息到指定的 MQTT topic")]
    public async Task<string> Publish(
        [Description("Topic 路徑")] string topic,
        [Description("訊息內容")] string message,
        [Description("QoS 等級（0/1/2）")] int qos = 0,
        [Description("是否保留訊息")] bool retain = false,
        [Description(WriteConnectionGuard.ParameterDescription)] string connection = ""
    ) {
        if (WriteConnectionGuard.CheckMissing(connection) is string blocked) {
            return blocked;
        }

        if (qos is < 0 or > 2) {
            return ToolResponse.Error($"QoS must be 0, 1, or 2 (got {qos}).");
        }

        try {
            IMqttClient client = await registry.ConnectAsync(connection);

            MqttQualityOfServiceLevel mqttQos = (MqttQualityOfServiceLevel)qos;

            MqttApplicationMessage msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(message)
                .WithQualityOfServiceLevel(mqttQos)
                .WithRetainFlag(retain)
                .Build();

            await client.PublishAsync(msg, CancellationToken.None);
            await client.DisconnectAsync();
            client.Dispose();

            return ToolResponse.Ok(new {
                topic,
                qos,
                retain,
            }, "Message published.");
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 訂閱 topic 並等待最多 timeout 秒，接收一則訊息
    /// </summary>
    [McpServerTool, Description("訂閱 topic 並等待最多 timeout 秒，接收一則訊息")]
    public async Task<string> SubscribeOnce(
        [Description("Topic 或萬用字元（# / +）")] string topic,
        [Description("等待秒數，預設 5")] int timeout = 5,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            int maxResponseBytes = MaxResponseBytes;
            MqttPayloadCollector collector = new(new ResponseBudget(maxResponseBytes));
            TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            IMqttClient client = await registry.ConnectAsync(connection);

            client.ApplicationMessageReceivedAsync += e => {
                collector.Add(
                    e.ApplicationMessage.Topic,
                    e.ApplicationMessage.PayloadSegment,
                    (int)e.ApplicationMessage.QualityOfServiceLevel
                );
                tcs.TrySetResult(true);
                return Task.CompletedTask;
            };

            await client.SubscribeAsync(
                new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f.WithTopic(topic))
                    .Build()
            );

            await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeout)));
            await client.DisconnectAsync();
            client.Dispose();

            IReadOnlyList<object> data = collector.CreateResponseData(maxResponseBytes);

            if (data.Count == 0) {
                return ToolResponse.Empty("No message received within timeout.", data);
            }

            return collector.Truncated
                ? ToolResponse.Ok(data, ResponseTruncation.Message(collector.ObservedBytes, false, maxResponseBytes))
                : ToolResponse.Ok(data);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 取得 MQTT Broker 統計資訊（透過 $SYS topic）
    /// </summary>
    [McpServerTool, Description("取得 MQTT Broker 統計資訊（透過 $SYS topic）")]
    public async Task<string> GetBrokerStatus(
        [Description("連線名稱")] string connection = ""
    ) {
        Dictionary<string, string> stats = new();
        ResponseBudget budget = new(MaxResponseBytes);
        object syncRoot = new();

        try {
            IMqttClient client = await registry.ConnectAsync(connection);

            client.ApplicationMessageReceivedAsync += e => {
                lock (syncRoot) {
                    RecordStatus(stats, budget, e.ApplicationMessage.Topic, e.ApplicationMessage.PayloadSegment);
                }

                return Task.CompletedTask;
            };

            await client.SubscribeAsync(
                new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f.WithTopic("$SYS/#"))
                    .Build()
            );

            await Task.Delay(TimeSpan.FromSeconds(2));
            await client.DisconnectAsync();
            client.Dispose();

            Dictionary<string, string> output;

            lock (syncRoot) {
                output = stats
                    .OrderBy(kv => kv.Key)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            return output.Count > 0
                ? ToolResponse.Ok(output)
                : ToolResponse.Empty("No stats available (broker may not expose $SYS).", output);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Records a <c>$SYS</c> message when its topic matches one of the broker status keys.
    /// Topics that match no key are dropped without decoding the payload, and the payload of a kept topic is cut to the budget.
    /// The caller must synchronize access to <paramref name="stats"/> and <paramref name="budget"/>.
    /// </summary>
    /// <param name="stats">The status values collected so far, keyed by topic.</param>
    /// <param name="budget">The budget that limits the payload bytes of the kept messages.</param>
    /// <param name="topic">The topic the message was published to.</param>
    /// <param name="payload">The raw payload bytes.</param>
    /// <returns><see langword="true"/> when the message was kept; otherwise <see langword="false"/>.</returns>
    public static bool RecordStatus(
        IDictionary<string, string> stats,
        ResponseBudget budget,
        string topic,
        ArraySegment<byte> payload
    ) {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(budget);

        if (!StatusInterestKeys.Any(k => topic.Contains(k))) {
            return false;
        }

        long accepted = budget.TakeUpTo(payload.Count);
        stats[topic] = Utf8Text.DecodePrefix(payload.AsSpan(0, (int)accepted), accepted < payload.Count);
        return true;
    }
}
