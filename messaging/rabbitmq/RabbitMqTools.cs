using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.RabbitMq;

/// <summary>
/// Provides MCP tools for managing and messaging through RabbitMQ via the Management API.
/// </summary>
[McpServerToolType]
public sealed class RabbitMqTools {
    private readonly ConnectionRegistry registry;
    private readonly IDotEnvSnapshotProvider snapshotProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="RabbitMqTools"/> with the specified connection registry.
    /// </summary>
    /// <param name="registry">The connection registry.</param>
    /// <param name="snapshotProvider">The .env snapshot provider used to read MCPLAB_MAX_RESPONSE_BYTES.</param>
    public RabbitMqTools(ConnectionRegistry registry, IDotEnvSnapshotProvider snapshotProvider) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        this.registry = registry;
        this.snapshotProvider = snapshotProvider;
    }

    private int MaxResponseBytes => ServiceSettings.Common.MaxResponseBytes.Read(snapshotProvider.Current);

    /// <summary>
    /// 列出所有已設定的 RabbitMQ 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 RabbitMQ 連線")]
    public string ListConnections() {
        return ToolResponse.Ok(
            registry.All.Select(kv => new {
                name = kv.Key,
                environment = registry.EnvironmentOf(kv.Key),
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

            string body = await GetJsonTextAsync(http, "api/whoami");
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

            string body = await GetJsonTextAsync(http, $"api/queues/{Uri.EscapeDataString(vhost)}");
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

            string body = await GetJsonTextAsync(http, $"api/exchanges/{Uri.EscapeDataString(vhost)}");
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

            string body = await GetJsonTextAsync(
                http,
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

            string body = await GetJsonTextAsync(http, "api/overview");
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
        [Description(WriteConnectionGuard.ParameterDescription)] string connection = ""
    ) {
        if (WriteConnectionGuard.CheckMissing(connection) is string blocked) {
            return blocked;
        }

        try {
            using HttpClient http = registry.CreateClient(connection);

            string payload = JsonSerializer.Serialize(new {
                properties = new { },
                routing_key = routingKey,
                payload = message,
                payload_encoding = "string",
            });
            string url = $"api/exchanges/{Uri.EscapeDataString(vhost)}/{Uri.EscapeDataString(exchange)}/publish";

            using HttpRequestMessage request = new(HttpMethod.Post, url) {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using BoundedResponse resp = await BoundedHttpContent.SendAsync(http, request, MaxResponseBytes);
            resp.Response.EnsureSuccessStatusCode();

            // The publish response is a few bytes; a body past the limit is treated as not routed.
            bool routed = !resp.Body.Truncated && IsRouted(resp.Body.ToText());
            object data = new {
                exchange,
                routing_key = routingKey,
                routed,
            };

            return routed
                ? ToolResponse.Ok(data, "Message published.")
                : ToolResponse.Error("Message was not routed to any queue. Check the exchange bindings and routing key.", data);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    private async Task<string> GetJsonTextAsync(HttpClient http, string path) {
        int maxBytes = MaxResponseBytes;
        using BoundedResponse response = await BoundedHttpContent.GetAsync(http, path, maxBytes);
        response.Response.EnsureSuccessStatusCode();

        // A JSON document cut at the limit cannot be summarized, so exceeding it is an error.
        return response.Body.RequireComplete(maxBytes);
    }

    /// <summary>
    /// Reads the <c>routed</c> flag from a Management API publish response.
    /// </summary>
    public static bool IsRouted(string responseBody) {
        try {
            return JsonNode.Parse(responseBody)?["routed"]?.GetValue<bool>() ?? false;
        } catch (Exception ex) when (ex is JsonException or InvalidOperationException) {
            return false;
        }
    }
}
