namespace CloudyWing.McpLab.Mosquitto;

/// <summary>
/// Provides MCP tools for publishing to and subscribing from MQTT brokers.
/// </summary>
[McpServerToolType]
public sealed class MqttTools {
    private static readonly JsonSerializerOptions JsonCompact = new() { WriteIndented = false };
    private readonly ConnectionRegistry registry;

    /// <summary>
    /// Initializes a new instance of <see cref="MqttTools"/> with the specified connection registry.
    /// </summary>
    public MqttTools(ConnectionRegistry registry) {
        this.registry = registry;
    }

    /// <summary>
    /// 列出所有已設定的 MQTT 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 MQTT 連線")]
    public string ListConnections() =>
        JsonSerializer.Serialize(
            registry.All.Select(kv => new {
                name = kv.Key,
                host = kv.Value.Host,
                port = kv.Value.Port,
                user = kv.Value.User,
            }), JsonCompact);

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
                    ? $"OK: Broker acknowledged subscription (result={rc})."
                    : $"Error: Broker denied subscription ({rc}).";
            } finally {
                await client.DisconnectAsync();
                client.Dispose();
            }
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
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
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            IMqttClient client = await registry.ConnectAsync(connection);

            MqttQualityOfServiceLevel mqttQos = qos switch {
                1 => MqttQualityOfServiceLevel.AtLeastOnce,
                2 => MqttQualityOfServiceLevel.ExactlyOnce,
                _ => MqttQualityOfServiceLevel.AtMostOnce,
            };

            MqttApplicationMessage msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(message)
                .WithQualityOfServiceLevel(mqttQos)
                .WithRetainFlag(retain)
                .Build();

            await client.PublishAsync(msg, CancellationToken.None);
            await client.DisconnectAsync();
            client.Dispose();

            return $"Published to '{topic}' (qos={qos}, retain={retain})";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
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
            List<object> messages = [];
            TaskCompletionSource<bool> tcs = new();

            IMqttClient client = await registry.ConnectAsync(connection);

            client.ApplicationMessageReceivedAsync += e => {
                messages.Add(new {
                    topic = e.ApplicationMessage.Topic,
                    payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment),
                    qos = (int)e.ApplicationMessage.QualityOfServiceLevel,
                });
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

            return messages.Count > 0
                ? JsonSerializer.Serialize(messages, JsonCompact)
                : "No message received within timeout.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
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

        try {
            IMqttClient client = await registry.ConnectAsync(connection);

            client.ApplicationMessageReceivedAsync += e => {
                string key = e.ApplicationMessage.Topic;
                string val = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                stats[key] = val;
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

            string[] interestKeys = [
                "version", "uptime", "clients/connected",
                "messages/received", "messages/sent",
                "bytes/received", "bytes/sent",
            ];

            IOrderedEnumerable<KeyValuePair<string, string>> filtered = stats
                .Where(kv => interestKeys.Any(k => kv.Key.Contains(k)))
                .OrderBy(kv => kv.Key);

            return filtered.Any()
                ? string.Join("\n", filtered.Select(kv => $"{kv.Key}: {kv.Value}"))
                : "No stats available (broker may not expose $SYS).";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }
}
