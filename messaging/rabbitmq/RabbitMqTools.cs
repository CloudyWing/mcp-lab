namespace CloudyWing.McpLab.RabbitMq;

/// <summary>
/// Provides MCP tools for managing and messaging through RabbitMQ via the Management API.
/// </summary>
[McpServerToolType]
public sealed class RabbitMqTools {
    private readonly ConnectionRegistry registry;

    /// <summary>
    /// Initializes a new instance of <see cref="RabbitMqTools"/> with the specified connection registry.
    /// </summary>
    public RabbitMqTools(ConnectionRegistry registry) {
        this.registry = registry;
    }

    /// <summary>
    /// 列出所有已設定的 RabbitMQ 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 RabbitMQ 連線")]
    public string ListConnections() {
        return ToolResponse.Ok(
            registry.All.Select(kv => new {
                name = kv.Key,
                host = kv.Value.Host,
                mgmt_port = kv.Value.MgmtPort,
                user = kv.Value.User,
            })
        );
    }

    /// <summary>
    /// 測試 RabbitMQ Management API 連線是否正常
    /// </summary>
    [McpServerTool, Description("測試 RabbitMQ Management API 連線是否正常")]
    public async Task<string> PingConnection(
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);

            string body = await http.GetStringAsync("api/whoami");
            JsonNode? node = JsonNode.Parse(body);

            return ToolResponse.Ok(new {
                user = node?["name"]?.ToString(),
                tags = node?["tags"],
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 列出指定 vhost 下的所有 Queue
    /// </summary>
    [McpServerTool, Description("列出指定 vhost 下的所有 Queue")]
    public async Task<string> ListQueues(
        [Description("Virtual host，預設 /")] string vhost = "/",
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);

            string body = await http.GetStringAsync($"api/queues/{Uri.EscapeDataString(vhost)}");
            JsonArray arr = JsonNode.Parse(body)?.AsArray() ?? [];
            List<object> queues = [];

            foreach (JsonNode? q in arr) {
                queues.Add(new {
                    name = q?["name"]?.ToString(),
                    messages = q?["messages"]?.ToString(),
                    consumers = q?["consumers"]?.ToString(),
                });
            }

            return queues.Count > 0
                ? ToolResponse.Ok(queues)
                : ToolResponse.Empty("No queues found.", queues);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 列出指定 vhost 下的所有 Exchange
    /// </summary>
    [McpServerTool, Description("列出指定 vhost 下的所有 Exchange")]
    public async Task<string> ListExchanges(
        [Description("Virtual host，預設 /")] string vhost = "/",
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);

            string body = await http.GetStringAsync($"api/exchanges/{Uri.EscapeDataString(vhost)}");
            JsonArray arr = JsonNode.Parse(body)?.AsArray() ?? [];
            List<object> exchanges = [];

            foreach (JsonNode? exNode in arr) {
                string name = exNode?["name"]?.ToString() ?? string.Empty;

                if (!string.IsNullOrEmpty(name)) {
                    exchanges.Add(new {
                        name,
                        type = exNode?["type"]?.ToString(),
                        durable = exNode?["durable"]?.ToString(),
                    });
                }
            }

            return exchanges.Count > 0
                ? ToolResponse.Ok(exchanges)
                : ToolResponse.Empty("No exchanges found.", exchanges);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 取得指定 Queue 的詳細資訊
    /// </summary>
    [McpServerTool, Description("取得指定 Queue 的詳細資訊")]
    public async Task<string> GetQueueDetails(
        [Description("Queue 名稱")] string queue,
        [Description("Virtual host，預設 /")] string vhost = "/",
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);

            string body = await http.GetStringAsync(
                $"api/queues/{Uri.EscapeDataString(vhost)}/{Uri.EscapeDataString(queue)}"
            );
            JsonNode? node = JsonNode.Parse(body);

            object summary = new {
                name = node?["name"]?.ToString(),
                messages = node?["messages"]?.ToString(),
                consumers = node?["consumers"]?.ToString(),
                state = node?["state"]?.ToString(),
                durable = node?["durable"]?.ToString(),
                auto_delete = node?["auto_delete"]?.ToString(),
                message_stats = node?["message_stats"],
            };

            return ToolResponse.Ok(summary);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 取得 RabbitMQ 系統概覽（版本、訊息統計等）
    /// </summary>
    [McpServerTool, Description("取得 RabbitMQ 系統概覽（版本、訊息統計等）")]
    public async Task<string> GetOverview(
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);

            string body = await http.GetStringAsync("api/overview");
            JsonNode? node = JsonNode.Parse(body);

            object summary = new {
                cluster_name = node?["cluster_name"]?.ToString(),
                rabbitmq_version = node?["rabbitmq_version"]?.ToString(),
                erlang_version = node?["erlang_version"]?.ToString(),
                message_stats = node?["message_stats"],
                queue_totals = node?["queue_totals"],
                object_totals = node?["object_totals"],
            };

            return ToolResponse.Ok(summary);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 透過 Management API 發布訊息到指定 Exchange
    /// </summary>
    [McpServerTool, Description("透過 Management API 發布訊息到指定 Exchange")]
    public async Task<string> PublishMessage(
        [Description("Exchange 名稱")] string exchange,
        [Description("Routing key")] string routingKey,
        [Description("訊息內容")] string message,
        [Description("Virtual host，預設 /")] string vhost = "/",
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);

            string payload = JsonSerializer.Serialize(new {
                properties = new { },
                routing_key = routingKey,
                payload = message,
                payload_encoding = "string",
            });
            string url = $"api/exchanges/{Uri.EscapeDataString(vhost)}/{Uri.EscapeDataString(exchange)}/publish";

            HttpResponseMessage resp = await http.PostAsync(url,
                new StringContent(payload, Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();

            return ToolResponse.Ok(new {
                exchange,
                routing_key = routingKey,
            }, "Message published.");
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }
}
