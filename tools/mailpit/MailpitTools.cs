namespace CloudyWing.McpLab.Mailpit;

/// <summary>
/// Provides read-only MCP tools for inspecting Mailpit messages.
/// </summary>
[McpServerToolType]
public sealed class MailpitTools {
    private const int DefaultListLimit = 25;
    private const int MaxListLimit = 100;
    private const int DefaultSourceLimit = 20000;
    private const int MaxSourceLimit = 200000;
    private const int DefaultWaitTimeoutSeconds = 10;
    private const int MaxWaitTimeoutSeconds = 60;
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(500);
    private readonly ConnectionRegistry registry;

    /// <summary>
    /// Initializes a new instance of <see cref="MailpitTools"/> with the connection registry.
    /// </summary>
    public MailpitTools(ConnectionRegistry registry) {
        this.registry = registry;
    }

    /// <summary>
    /// Lists all configured Mailpit connections.
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 Mailpit 連線")]
    public string ListConnections() =>
        ToolResponse.Ok(registry.All.Select(kv => new { name = kv.Key, url = kv.Value.Url }));

    /// <summary>
    /// Tests whether the Mailpit API is reachable and returns basic message counts.
    /// </summary>
    [McpServerTool, Description("測試 Mailpit API 連線是否正常，並回傳基本信件計數")]
    public async Task<string> PingConnection(
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);
            JsonObject info = await GetJsonObjectAsync(http, "api/v1/info").ConfigureAwait(false);

            return ToolResponse.Ok(new {
                version = GetString(info, "Version"),
                messages = GetNumber(info, "Messages"),
                unread = GetNumber(info, "Unread"),
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Lists recent Mailpit messages ordered from newest to oldest.
    /// </summary>
    [McpServerTool, Description("列出最新 Mailpit 信件摘要，不會標記已讀")]
    public async Task<string> ListMessages(
        [Description("起始位移，預設 0")] int start = 0,
        [Description("最大回傳信件數，預設 25，上限 100")] int limit = 0,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            int safeStart = Math.Max(start, 0);
            int safeLimit = NormalizeListLimit(limit);
            using HttpClient http = registry.CreateClient(connection);
            JsonObject result = await GetMessagesAsync(http, "api/v1/messages", safeStart, safeLimit)
                .ConfigureAwait(false);

            return CreateMessagesResponse(result, safeStart, safeLimit);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Searches Mailpit messages with the Mailpit search query syntax.
    /// </summary>
    [McpServerTool, Description("搜尋 Mailpit 信件摘要，不會標記已讀")]
    public async Task<string> SearchMessages(
        [Description("Mailpit 搜尋語法，例如 subject:welcome 或 user@example.com")] string query,
        [Description("起始位移，預設 0")] int start = 0,
        [Description("最大回傳信件數，預設 25，上限 100")] int limit = 0,
        [Description("連線名稱")] string connection = ""
    ) {
        if (string.IsNullOrWhiteSpace(query)) {
            return ToolResponse.Error("Query is required.");
        }

        try {
            int safeStart = Math.Max(start, 0);
            int safeLimit = NormalizeListLimit(limit);
            string path = $"api/v1/search?query={Uri.EscapeDataString(query)}";
            using HttpClient http = registry.CreateClient(connection);
            JsonObject result = await GetMessagesAsync(http, path, safeStart, safeLimit).ConfigureAwait(false);

            return CreateMessagesResponse(result, safeStart, safeLimit);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Waits for a message summary that matches the supplied filters.
    /// </summary>
    [McpServerTool, Description("等待符合條件的 Mailpit 信件出現，不會標記已讀")]
    public async Task<string> WaitForMessage(
        [Description("Mailpit 搜尋語法，空字串表示只用 to 或 subjectContains 篩選")] string query = "",
        [Description("收件者 email，空字串表示不篩選")] string to = "",
        [Description("主旨需包含的文字，空字串表示不篩選")] string subjectContains = "",
        [Description("等待秒數，預設 10，上限 60")] int timeoutSeconds = 0,
        [Description("連線名稱")] string connection = ""
    ) {
        if (string.IsNullOrWhiteSpace(query)
            && string.IsNullOrWhiteSpace(to)
            && string.IsNullOrWhiteSpace(subjectContains)) {
            return ToolResponse.Error("At least one filter is required.");
        }

        try {
            int safeTimeout = ToolRuntimeOptions.NormalizeRequestedInt32(
                timeoutSeconds,
                DefaultWaitTimeoutSeconds,
                1,
                MaxWaitTimeoutSeconds
            );
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(safeTimeout);
            using HttpClient http = registry.CreateClient(connection);

            while (DateTimeOffset.UtcNow <= deadline) {
                JsonObject result = string.IsNullOrWhiteSpace(query)
                    ? await GetMessagesAsync(http, "api/v1/messages", 0, MaxListLimit).ConfigureAwait(false)
                    : await GetMessagesAsync(
                        http,
                        $"api/v1/search?query={Uri.EscapeDataString(query)}",
                        0,
                        MaxListLimit
                    ).ConfigureAwait(false);

                JsonNode? match = GetMessageArray(result).FirstOrDefault(
                    message => MailpitMessageMatcher.Matches(message, to, subjectContains)
                );

                if (match is not null) {
                    return ToolResponse.Ok(new {
                        timeout_seconds = safeTimeout,
                        message = ToMessageSummary(match),
                    });
                }

                await Task.Delay(PollDelay).ConfigureAwait(false);
            }

            return ToolResponse.Empty("No matching message found before timeout.", new {
                timeout_seconds = safeTimeout,
                query,
                to,
                subject_contains = subjectContains,
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Gets message headers without marking the message as read.
    /// </summary>
    [McpServerTool, Description("取得指定信件 headers，不會標記已讀；ID 可使用 latest")]
    public async Task<string> GetMessageHeaders(
        [Description("Mailpit 信件 ID，或 latest")] string id,
        [Description("連線名稱")] string connection = ""
    ) {
        if (string.IsNullOrWhiteSpace(id)) {
            return ToolResponse.Error("Message ID is required.");
        }

        try {
            using HttpClient http = registry.CreateClient(connection);
            JsonObject headers = await GetJsonObjectAsync(
                http,
                $"api/v1/message/{Uri.EscapeDataString(id)}/headers"
            ).ConfigureAwait(false);

            return ToolResponse.Ok(new {
                id,
                headers,
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Gets the raw message source without marking the message as read.
    /// </summary>
    [McpServerTool, Description("取得指定信件 raw source，不會標記已讀；ID 可使用 latest")]
    public async Task<string> GetMessageSource(
        [Description("Mailpit 信件 ID，或 latest")] string id,
        [Description("最大回傳字元數，預設 20000，上限 200000")] int limit = 0,
        [Description("連線名稱")] string connection = ""
    ) {
        if (string.IsNullOrWhiteSpace(id)) {
            return ToolResponse.Error("Message ID is required.");
        }

        try {
            int safeLimit = ToolRuntimeOptions.NormalizeRequestedInt32(
                limit,
                DefaultSourceLimit,
                1,
                MaxSourceLimit
            );
            using HttpClient http = registry.CreateClient(connection);
            string source = await GetStringAsync(
                http,
                $"api/v1/message/{Uri.EscapeDataString(id)}/raw"
            ).ConfigureAwait(false);

            return ToolResponse.Ok(new {
                id,
                limit = safeLimit,
                source_length = source.Length,
                truncated = source.Length > safeLimit,
                source = LimitText(source, safeLimit),
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    private static int NormalizeListLimit(int limit) =>
        ToolRuntimeOptions.NormalizeRequestedInt32(limit, DefaultListLimit, 1, MaxListLimit);

    private static async Task<JsonObject> GetMessagesAsync(
        HttpClient http,
        string path,
        int start,
        int limit
    ) {
        string separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return await GetJsonObjectAsync(http, $"{path}{separator}start={start}&limit={limit}").ConfigureAwait(false);
    }

    private static async Task<JsonObject> GetJsonObjectAsync(HttpClient http, string path) {
        string body = await GetStringAsync(http, path).ConfigureAwait(false);

        return JsonNode.Parse(body)?.AsObject()
            ?? throw new InvalidOperationException("Mailpit returned an invalid JSON object.");
    }

    private static async Task<string> GetStringAsync(HttpClient http, string path) {
        using HttpResponseMessage response = await http.GetAsync(path).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            string message = string.IsNullOrWhiteSpace(body)
                ? $"{(int)response.StatusCode} {response.StatusCode}"
                : body;

            throw new InvalidOperationException($"Mailpit returned {message}");
        }

        return body;
    }

    private static string CreateMessagesResponse(JsonObject result, int start, int limit) {
        object[] messages = GetMessageArray(result)
            .Select(ToMessageSummary)
            .ToArray();

        object data = new {
            total = GetNumber(result, "total"),
            unread = GetNumber(result, "unread"),
            count = messages.Length,
            start,
            limit,
            messages,
        };

        return messages.Length > 0
            ? ToolResponse.Ok(data)
            : ToolResponse.Empty("No messages found.", data);
    }

    private static JsonArray GetMessageArray(JsonObject result) =>
        result["messages"] as JsonArray ?? [];

    private static object ToMessageSummary(JsonNode? message) => new {
        id = GetString(message, "ID"),
        message_id = GetString(message, "MessageID"),
        read = GetBool(message, "Read"),
        from = ToAddress(message?["From"]),
        to = ToAddressList(message?["To"]),
        cc = ToAddressList(message?["Cc"]),
        bcc = ToAddressList(message?["Bcc"]),
        reply_to = ToAddressList(message?["ReplyTo"]),
        subject = GetString(message, "Subject"),
        created = GetString(message, "Created"),
        username = GetString(message, "Username"),
        tags = ToStringArray(message?["Tags"]),
        size = GetNumber(message, "Size"),
        attachments = GetNumber(message, "Attachments"),
        snippet = GetString(message, "Snippet"),
    };

    private static object ToAddress(JsonNode? node) => new {
        name = GetString(node, "Name"),
        address = GetString(node, "Address"),
    };

    private static object[] ToAddressList(JsonNode? node) {
        if (node is not JsonArray arr) {
            return [];
        }

        return arr.Select(ToAddress).ToArray();
    }

    private static string[] ToStringArray(JsonNode? node) {
        if (node is not JsonArray arr) {
            return [];
        }

        return arr.Select(item => item?.ToString() ?? "").ToArray();
    }

    private static string LimitText(string value, int limit) =>
        value.Length <= limit ? value : value[..limit];

    private static string GetString(JsonNode? node, string property) =>
        node?[property]?.ToString() ?? "";

    private static bool GetBool(JsonNode? node, string property) {
        JsonNode? current = node?[property];

        return current is not null && bool.TryParse(current.ToString(), out bool value) && value;
    }

    private static double GetNumber(JsonNode? node, string property) {
        JsonNode? current = node?[property];

        return current is not null && double.TryParse(current.ToString(), out double value) ? value : 0;
    }
}
