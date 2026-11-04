namespace CloudyWing.McpLab.Elastic;

/// <summary>
/// Validates Elasticsearch write operations.
/// Blocks: match_all anywhere in a delete query, missing or empty query, and multi-index targets.
/// </summary>
public static class ElasticGuard {
    /// <summary>
    /// Throws when the query body is empty, contains match_all, or has no concrete query object.
    /// </summary>
    public static void ValidateQueryBody(string queryJson, string operation) {
        if (string.IsNullOrWhiteSpace(queryJson)) {
            throw new InvalidOperationException($"{operation} requires a non-empty query. A bare match_all is not allowed.");
        }

        JsonNode? node;

        try {
            node = JsonNode.Parse(queryJson);
        } catch (JsonException) {
            throw new InvalidOperationException("Invalid JSON query body.");
        }

        if (node is not JsonObject obj) {
            throw new InvalidOperationException($"{operation} requires a JSON object query body.");
        }

        if (ContainsMatchAll(obj)) {
            throw new InvalidOperationException($"{operation} with match_all is not allowed (equivalent to TRUNCATE).");
        }

        if (obj["query"] is not JsonObject query || query.Count == 0) {
            throw new InvalidOperationException($"{operation} requires a non-empty \"query\" object.");
        }
    }

    /// <summary>
    /// Throws when the index targets more than one concrete index (wildcard, list, or _all).
    /// </summary>
    public static void ValidateSingleIndex(string index, string operation) {
        string value = index?.Trim() ?? "";

        if (value.Length == 0) {
            throw new InvalidOperationException($"{operation} requires an index name.");
        }

        if (value.IndexOfAny(['*', '?', ',']) >= 0 || value.StartsWith('_') || value.StartsWith('-')) {
            throw new InvalidOperationException(
                $"{operation} must target a single index. Wildcards, comma-separated lists, and _all are not allowed.");
        }
    }

    private static bool ContainsMatchAll(JsonNode? node) {
        return node switch {
            JsonObject obj => obj.Any(pair => pair.Key == "match_all" || ContainsMatchAll(pair.Value)),
            JsonArray array => array.Any(ContainsMatchAll),
            _ => false,
        };
    }
}
