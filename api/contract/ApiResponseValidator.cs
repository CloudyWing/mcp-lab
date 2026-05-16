namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Performs bounded OpenAPI response schema checks.
/// </summary>
public static class ApiResponseValidator {
    /// <summary>
    /// Validates a JSON response body against the response schema when present.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        OpenApiSpec spec,
        string path,
        string method,
        int statusCode,
        string responseBody,
        int maxIssues
    ) {
        List<string> issues = [];
        JsonObject? response = spec.GetResponse(path, method, statusCode);

        if (response is null) {
            issues.Add($"Response status {statusCode} is not defined for {method.ToUpperInvariant()} {path}.");
            return issues;
        }

        JsonObject? schema = GetJsonSchema(response);

        if (schema is null || string.IsNullOrWhiteSpace(responseBody)) {
            return issues;
        }

        JsonNode? body = JsonNode.Parse(responseBody);
        ValidateNode("$", body, schema, issues, maxIssues);

        return issues;
    }

    private static JsonObject? GetJsonSchema(JsonObject response) {
        if (response["content"] is not JsonObject content) {
            return null;
        }

        JsonObject? media = content["application/json"] as JsonObject
            ?? content.FirstOrDefault(item => item.Key.EndsWith("+json", StringComparison.OrdinalIgnoreCase)).Value
                as JsonObject;

        return media?["schema"] as JsonObject;
    }

    private static void ValidateNode(
        string path,
        JsonNode? node,
        JsonObject schema,
        ICollection<string> issues,
        int maxIssues
    ) {
        if (issues.Count >= maxIssues) {
            return;
        }

        string type = schema["type"]?.GetValue<string>() ?? "";

        if (!IsExpectedType(node, type)) {
            issues.Add($"{path} expected {type}.");
            return;
        }

        if (type == "object" && schema["required"] is JsonArray required) {
            JsonObject? obj = node as JsonObject;

            foreach (JsonNode? item in required) {
                string name = item?.GetValue<string>() ?? "";

                if (!string.IsNullOrWhiteSpace(name) && obj?[name] is null) {
                    issues.Add($"{path}.{name} is required.");
                }

                if (issues.Count >= maxIssues) {
                    return;
                }
            }
        }

        if (type == "object" && node is JsonObject jsonObject && schema["properties"] is JsonObject properties) {
            foreach ((string propertyName, JsonNode? propertySchema) in properties) {
                if (jsonObject[propertyName] is JsonNode child && propertySchema is JsonObject childSchema) {
                    ValidateNode($"{path}.{propertyName}", child, childSchema, issues, maxIssues);
                }
            }
        }

        if (type == "array" && node is JsonArray array && schema["items"] is JsonObject itemSchema) {
            for (int i = 0; i < array.Count && i < 20; i++) {
                ValidateNode($"{path}[{i}]", array[i], itemSchema, issues, maxIssues);
            }
        }
    }

    private static bool IsExpectedType(JsonNode? node, string type) {
        if (string.IsNullOrWhiteSpace(type) || node is null) {
            return true;
        }

        return type switch {
            "object" => node is JsonObject,
            "array" => node is JsonArray,
            "string" => node is JsonValue value && value.TryGetValue(out string? _),
            "integer" => node is JsonValue value && value.TryGetValue(out int _),
            "number" => node is JsonValue value && value.TryGetValue(out decimal _),
            "boolean" => node is JsonValue value && value.TryGetValue(out bool _),
            _ => true,
        };
    }
}
