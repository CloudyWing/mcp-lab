namespace CloudyWing.McpLab.Elastic;

/// <summary>
/// Provides MCP tools for indexing, searching, and managing Elasticsearch clusters.
/// </summary>
[McpServerToolType]
public sealed class ElasticTools {
    private static readonly JsonSerializerOptions JsonPretty = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions JsonCompact = new() { WriteIndented = false };
    private static readonly string[] Value = ["*"];
    private readonly ConnectionRegistry registry;

    /// <summary>
    /// Initializes a new instance of <see cref="ElasticTools"/> with the specified connection registry.
    /// </summary>
    public ElasticTools(ConnectionRegistry registry) {
        this.registry = registry;
    }

    private static int DefaultSize =>
        int.TryParse(Environment.GetEnvironmentVariable("MAX_ROWS"), out int maxRows)
            ? maxRows : 10;

    /// <summary>
    /// 列出所有已設定的 Elasticsearch 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 Elasticsearch 連線")]
    public string ListConnections() =>
        JsonSerializer.Serialize(
            registry.All.Select(kv => new { name = kv.Key, url = kv.Value.Url, user = kv.Value.User }),
            JsonCompact
        );

    /// <summary>
    /// 測試 Elasticsearch 連線（回傳叢集名稱與版本）
    /// </summary>
    [McpServerTool, Description("測試 Elasticsearch 連線（回傳叢集名稱與版本）")]
    public async Task<string> PingConnection(
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);
            string body = await http.GetStringAsync("/");
            JsonNode? node = JsonNode.Parse(body);
            string name = node?["cluster_name"]?.ToString() ?? "";
            string ver = node?["version"]?["number"]?.ToString() ?? "";

            return $"OK: cluster={name} version={ver}";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 列出所有索引（名稱、文件數、大小）
    /// </summary>
    [McpServerTool, Description("列出所有索引（名稱、文件數、大小）")]
    public async Task<string> ListIndices(
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);
            string body = await http.GetStringAsync("/_cat/indices?format=json&s=index");
            JsonArray arr = JsonNode.Parse(body)?.AsArray() ?? [];
            List<string> lines = [];

            foreach (var item in arr) {
                lines.Add(
                    $"{item?["index"]}  docs={item?["docs.count"]}  size={item?["store.size"]}"
                );
            }

            return lines.Count > 0
                ? string.Join("\n", lines) : "No indices found.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 取得索引的 mapping 結構
    /// </summary>
    [McpServerTool, Description("取得索引的 mapping 結構")]
    public async Task<string> GetMapping(
        [Description("索引名稱")] string index,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);
            string body = await http.GetStringAsync($"/{Uri.EscapeDataString(index)}/_mapping");

            return JsonSerializer.Serialize(JsonNode.Parse(body), JsonPretty);
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 取得叢集健康狀態
    /// </summary>
    [McpServerTool, Description("取得叢集健康狀態")]
    public async Task<string> GetClusterHealth(
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);
            string body = await http.GetStringAsync("/_cluster/health");

            return JsonSerializer.Serialize(JsonNode.Parse(body), JsonPretty);
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 搜尋文件。query_body 為 JSON 格式的 ES 查詢，或純文字（自動轉 match 查詢）
    /// </summary>
    [McpServerTool, Description("搜尋文件。query_body 為 JSON 格式的 ES 查詢，或純文字（自動轉 match 查詢）")]
    public async Task<string> Search(
        [Description("索引名稱")] string index,
        [Description("查詢 JSON 或關鍵字")] string queryBody,
        [Description("回傳筆數，預設 10")] int size = 0,
        [Description("連線名稱")] string connection = ""
    ) {
        return await RunSearch(index, queryBody, size, connection);
    }

    /// <summary>
    /// 搜尋文件（Base64）。避免傳輸過程清洗字元。
    /// </summary>
    [McpServerTool, Description(
        "搜尋文件（Base64）。避免傳輸過程清洗字元。" +
        "當查詢 JSON 被清洗或內容失真時，請改用本工具。")]
    public async Task<string> SearchBase64(
        [Description("索引名稱")] string index,
        [Description("Base64 編碼的查詢 JSON 或關鍵字")] string queryBodyBase64,
        [Description("回傳筆數，預設 10")] int size = 0,
        [Description("連線名稱")] string connection = ""
    ) {
        if (!TryDecodeBase64(queryBodyBase64, out string queryBody, out string error)) {
            return $"Error: {error}";
        }

        return await RunSearch(index, queryBody, size, connection);
    }

    private async Task<string> RunSearch(string index, string queryBody, int size, string connection) {
        if (size <= 0) {
            size = DefaultSize;
        }

        string body = BuildQueryBody(queryBody);

        try {
            using HttpClient http = registry.CreateClient(connection);
            HttpResponseMessage resp = await http.PostAsync(
                $"/{Uri.EscapeDataString(index)}/_search?size={size}",
                new StringContent(body, Encoding.UTF8, "application/json")
            );
            string responseBody = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) {
                return $"Error: {resp.StatusCode} — {responseBody}";
            }

            JsonNode? root = JsonNode.Parse(responseBody);
            string total = root?["hits"]?["total"]?["value"]?.ToString() ?? "?";
            JsonArray hits = root?["hits"]?["hits"]?.AsArray() ?? [];
            List<string> output = [$"Total: {total}"];

            foreach (JsonNode? hit in hits) {
                output.Add(JsonSerializer.Serialize(new {
                    _id = hit?["_id"]?.ToString(),
                    _score = hit?["_score"]?.ToString(),
                    _source = hit?["_source"],
                }, JsonCompact));
            }

            return string.Join("\n", output);
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 建立或覆寫指定 ID 的文件（id 留空則自動產生）
    /// </summary>
    [McpServerTool, Description("建立或覆寫指定 ID 的文件（id 留空則自動產生）")]
    public async Task<string> IndexDocument(
        [Description("索引名稱")] string index,
        [Description("JSON 格式的文件內容")] string document,
        [Description("文件 ID，省略則自動產生")] string id = "",
        [Description("連線名稱")] string connection = ""
    ) {
        return await RunIndexDocument(index, document, id, connection);
    }

    /// <summary>
    /// 建立或覆寫指定 ID 的文件（Base64）。避免傳輸過程清洗字元。
    /// </summary>
    [McpServerTool, Description(
        "建立或覆寫指定 ID 的文件（Base64）。避免傳輸過程清洗字元。" +
        "當文件 JSON 被清洗或內容失真時，請改用本工具。")]
    public async Task<string> IndexDocumentBase64(
        [Description("索引名稱")] string index,
        [Description("Base64 編碼的 JSON 文件內容")] string documentBase64,
        [Description("文件 ID，省略則自動產生")] string id = "",
        [Description("連線名稱")] string connection = ""
    ) {
        if (!TryDecodeBase64(documentBase64, out string document, out string error)) {
            return $"Error: {error}";
        }

        return await RunIndexDocument(index, document, id, connection);
    }

    private async Task<string> RunIndexDocument(string index, string document, string id, string connection) {
        try {
            JsonNode.Parse(document); // validate JSON
            using HttpClient http = registry.CreateClient(connection);

            string url = string.IsNullOrWhiteSpace(id)
                ? $"/{Uri.EscapeDataString(index)}/_doc"
                : $"/{Uri.EscapeDataString(index)}/_doc/{Uri.EscapeDataString(id)}";
            HttpMethod method = string.IsNullOrWhiteSpace(id) ? HttpMethod.Post : HttpMethod.Put;
            HttpResponseMessage resp = await http.SendAsync(new HttpRequestMessage(method, url) {
                Content = new StringContent(document, Encoding.UTF8, "application/json"),
            });
            string body = await resp.Content.ReadAsStringAsync();

            return resp.IsSuccessStatusCode ? body : $"Error: {resp.StatusCode} — {body}";
        } catch (JsonException) {
            return "Error: Invalid JSON document.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 刪除指定 ID 的文件
    /// </summary>
    [McpServerTool, Description("刪除指定 ID 的文件")]
    public async Task<string> DeleteDocument(
        [Description("索引名稱")] string index,
        [Description("文件 ID")] string id,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using HttpClient http = registry.CreateClient(connection);
            HttpResponseMessage resp = await http.DeleteAsync(
                $"/{Uri.EscapeDataString(index)}/_doc/{Uri.EscapeDataString(id)}");
            string body = await resp.Content.ReadAsStringAsync();

            return resp.IsSuccessStatusCode ? body : $"Error: {resp.StatusCode} — {body}";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description(
        "依查詢條件批次刪除文件（等同 DELETE...WHERE）。" +
        "禁止 match_all（等同 TRUNCATE）。query_body 必須包含具體過濾條件。")]
    public async Task<string> DeleteByQuery(
        [Description("索引名稱")] string index,
        [Description("JSON 查詢條件，必須包含具體 query（不可 match_all）")] string queryBody,
        [Description("連線名稱")] string connection = ""
    ) {
        return await RunDeleteByQuery(index, queryBody, connection);
    }

    /// <summary>
    /// 依查詢條件批次刪除文件（Base64）。避免傳輸過程清洗字元。
    /// </summary>
    [McpServerTool, Description(
        "依查詢條件批次刪除文件（Base64）。避免傳輸過程清洗字元。" +
        "當查詢 JSON 被清洗或內容失真時，請改用本工具。")]
    public async Task<string> DeleteByQueryBase64(
        [Description("索引名稱")] string index,
        [Description("Base64 編碼的 JSON 查詢條件")] string queryBodyBase64,
        [Description("連線名稱")] string connection = ""
    ) {
        if (!TryDecodeBase64(queryBodyBase64, out string queryBody, out string error)) {
            return $"Error: {error}";
        }

        return await RunDeleteByQuery(index, queryBody, connection);
    }

    private async Task<string> RunDeleteByQuery(string index, string queryBody, string connection) {
        try {
            ElasticGuard.ValidateQueryBody(queryBody, "DeleteByQuery");
            using HttpClient http = registry.CreateClient(connection);
            HttpResponseMessage resp = await http.PostAsync(
                $"/{Uri.EscapeDataString(index)}/_delete_by_query",
                new StringContent(queryBody, Encoding.UTF8, "application/json"));
            string body = await resp.Content.ReadAsStringAsync();

            return resp.IsSuccessStatusCode ? body : $"Error: {resp.StatusCode} — {body}";
        } catch (InvalidOperationException ex) {
            return $"Blocked: {ex.Message}";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    private static string BuildQueryBody(string queryBody) {
        try {
            return JsonNode.Parse(queryBody) is not null
                ? queryBody
                : throw new JsonException();
        } catch {
            return JsonSerializer.Serialize(new {
                query = new {
                    multi_match = new {
                        query = queryBody,
                        fields = Value,
                    },
                },
            });
        }
    }

    private static bool TryDecodeBase64(string input, out string value, out string error) {
        value = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input)) {
            error = "Payload is empty.";
            return false;
        }

        try {
            string normalized = NormalizeBase64(input);
            byte[] bytes = Convert.FromBase64String(normalized);
            value = Encoding.UTF8.GetString(bytes);

            if (string.IsNullOrWhiteSpace(value)) {
                error = "Decoded payload is empty.";
                return false;
            }

            MaybeLogBase64(input, normalized, value);
            return true;
        } catch (FormatException) {
            error = "Invalid Base64.";
            return false;
        }
    }

    private static void MaybeLogBase64(string raw, string normalized, string decoded) {
        if (Environment.GetEnvironmentVariable("MCP_LOG_BASE64") is not "1") {
            return;
        }

        string safeDecoded = decoded.Length <= 500 ? decoded : decoded[..500] + "...";
        Console.WriteLine($"[base64] raw={raw}");
        Console.WriteLine($"[base64] normalized={normalized}");
        Console.WriteLine($"[base64] decoded={safeDecoded}");
    }

    private static string NormalizeBase64(string input) {
        string value = input.Trim()
            .Replace('-', '+')
            .Replace('_', '/');
        int mod = value.Length % 4;

        if (mod == 2) {
            return value + "==";
        }

        if (mod == 3) {
            return value + "=";
        }

        return value;
    }
}
