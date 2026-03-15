namespace CloudyWing.McpLab.Elastic;

/// <summary>
/// Validates Elasticsearch write operations.
/// Blocks: delete index, match_all delete, match_all update.
/// </summary>
public static class ElasticGuard {
    /// <summary>
    /// Throws when query body is a bare match_all or empty (would affect every document).
    /// </summary>
    public static void ValidateQueryBody(string queryJson, string operation) {
        if (string.IsNullOrWhiteSpace(queryJson)) {
            throw new InvalidOperationException($"{operation} requires a non-empty query. A bare match_all is not allowed.");
        }

        try {
            JsonNode? node = JsonNode.Parse(queryJson);

            if (node is not JsonObject obj) {
                return;
            }

            // Detect: {"query":{"match_all":{}}} or {"match_all":{}}
            JsonNode queryNode = obj["query"] ?? obj;

            if (queryNode is JsonObject qObj && qObj.ContainsKey("match_all")) {
                throw new InvalidOperationException($"{operation} with match_all is not allowed (equivalent to TRUNCATE).");
            }
        } catch (JsonException) {
            throw new InvalidOperationException("Invalid JSON query body.");
        }
    }
}
