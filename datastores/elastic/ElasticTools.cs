namespace CloudyWing.McpLab.Elastic;

/// <summary>
/// Provides MCP tools for indexing, searching, and managing Elasticsearch clusters.
/// </summary>
[McpServerToolType]
public sealed class ElasticTools {
    private const int DefaultSearchSize = 10;
    private const int MaxSearchSize = 1000;
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
        ToolRuntimeOptions.GetEnvironmentInt32(
            ["ES_SEARCH_SIZE", "MAX_ROWS"],
            DefaultSearchSize,
            1,
            MaxSearchSize
        );

    /// <summary>
    /// 列出所有已設定的 Elasticsearch 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 Elasticsearch 連線")]
    public string ListConnections() =>
        ToolResponse.Ok(
            registry.All.Select(kv => new { name = kv.Key, url = kv.Value.Url, user = kv.Value.User }),
            "Configured Elasticsearch connections."
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

            return ToolResponse.Ok(new {
                cluster = name,
                version = ver,
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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
            List<object> indices = [];

            foreach (JsonNode? item in arr) {
                indices.Add(new {
                    index = item?["index"]?.ToString(),
                    docs = item?["docs.count"]?.ToString(),
                    size = item?["store.size"]?.ToString(),
                });
            }

            return indices.Count > 0
                ? ToolResponse.Ok(indices)
                : ToolResponse.Empty("No indices found.", indices);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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

            return ToolResponse.Ok(JsonNode.Parse(body));
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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

            return ToolResponse.Ok(JsonNode.Parse(body));
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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
        if (!Base64Payload.TryDecodeUtf8(
            queryBodyBase64,
            "Payload is empty.",
            "Decoded payload is empty.",
            out string queryBody,
            out string error
        )) {
            return ToolResponse.Error(error);
        }

        return await RunSearch(index, queryBody, size, connection);
    }

    private async Task<string> RunSearch(string index, string queryBody, int size, string connection) {
        int safeSize = ToolRuntimeOptions.NormalizeRequestedInt32(
            size,
            DefaultSize,
            1,
            MaxSearchSize
        );

        string body = BuildQueryBody(queryBody);

        try {
            using HttpClient http = registry.CreateClient(connection);
            HttpResponseMessage resp = await http.PostAsync(
                $"/{Uri.EscapeDataString(index)}/_search?size={safeSize}",
                new StringContent(body, Encoding.UTF8, "application/json")
            );
            string responseBody = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) {
                return ToolResponse.Error(
                    $"Elasticsearch returned {(int)resp.StatusCode} {resp.StatusCode}.",
                    ParseJsonOrText(responseBody)
                );
            }

            JsonNode? root = JsonNode.Parse(responseBody);
            string total = root?["hits"]?["total"]?["value"]?.ToString() ?? "0";
            JsonArray hits = root?["hits"]?["hits"]?.AsArray() ?? [];
            List<object> output = [];

            foreach (JsonNode? hit in hits) {
                output.Add(new {
                    _id = hit?["_id"]?.ToString(),
                    _score = hit?["_score"]?.ToString(),
                    _source = hit?["_source"],
                });
            }

            return ToolResponse.Ok(new {
                total,
                hits = output,
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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
        if (!Base64Payload.TryDecodeUtf8(
            documentBase64,
            "Payload is empty.",
            "Decoded payload is empty.",
            out string document,
            out string error
        )) {
            return ToolResponse.Error(error);
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

            return resp.IsSuccessStatusCode
                ? ToolResponse.Ok(ParseJsonOrText(body), "Document indexed.")
                : ToolResponse.Error(
                    $"Elasticsearch returned {(int)resp.StatusCode} {resp.StatusCode}.",
                    ParseJsonOrText(body)
                );
        } catch (JsonException) {
            return ToolResponse.Error("Invalid JSON document.");
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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

            return resp.IsSuccessStatusCode
                ? ToolResponse.Ok(ParseJsonOrText(body), "Document deleted.")
                : ToolResponse.Error(
                    $"Elasticsearch returned {(int)resp.StatusCode} {resp.StatusCode}.",
                    ParseJsonOrText(body)
                );
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 依查詢條件批次刪除文件。
    /// </summary>
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
        if (!Base64Payload.TryDecodeUtf8(
            queryBodyBase64,
            "Payload is empty.",
            "Decoded payload is empty.",
            out string queryBody,
            out string error
        )) {
            return ToolResponse.Error(error);
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

            return resp.IsSuccessStatusCode
                ? ToolResponse.Ok(ParseJsonOrText(body), "Delete by query completed.")
                : ToolResponse.Error(
                    $"Elasticsearch returned {(int)resp.StatusCode} {resp.StatusCode}.",
                    ParseJsonOrText(body)
                );
        } catch (InvalidOperationException ex) {
            return ToolResponse.Blocked(ex.Message);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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

    private static object? ParseJsonOrText(string body) {
        try {
            return JsonNode.Parse(body);
        } catch (JsonException) {
            return SensitiveDataSanitizer.Redact(body);
        }
    }
}
