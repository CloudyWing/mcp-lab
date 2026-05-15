using System.Text.Json;
using System.Text.Json.Nodes;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Creates consistent JSON envelopes for MCP tool responses.
/// </summary>
public static class ToolResponse {
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>
    /// Creates a successful response.
    /// </summary>
    public static string Ok(object? data = null, string message = "OK") =>
        Create(true, "ok", message, data);

    /// <summary>
    /// Creates an empty successful response.
    /// </summary>
    public static string Empty(string message, object? data = null) =>
        Create(true, "empty", message, data);

    /// <summary>
    /// Creates a response for a blocked operation.
    /// </summary>
    public static string Blocked(string message, object? data = null) =>
        Create(false, "blocked", message, data);

    /// <summary>
    /// Creates an error response.
    /// </summary>
    public static string Error(string message, object? data = null) =>
        Create(false, "error", SensitiveDataSanitizer.Redact(message), data);

    /// <summary>
    /// Creates an error response from an exception.
    /// </summary>
    public static string Error(Exception exception) => Error(exception.Message);

    private static string Create(bool ok, string kind, string message, object? data) {
        return JsonSerializer.Serialize(new {
            ok,
            kind,
            message = SensitiveDataSanitizer.Redact(message),
            data = RedactData(data),
        }, Json);
    }

    private static JsonNode? RedactData(object? data) {
        if (data is null) {
            return null;
        }

        JsonNode? node = JsonSerializer.SerializeToNode(data, Json);
        RedactNode(node);

        return node;
    }

    private static void RedactNode(JsonNode? node) {
        if (node is null) {
            return;
        }

        if (node is JsonArray array) {
            for (int i = 0; i < array.Count; i++) {
                JsonNode? item = array[i];

                if (TryRedactStringValue(item, out JsonNode? redacted)) {
                    array[i] = redacted;
                } else {
                    RedactNode(item);
                }
            }
        }

        if (node is JsonObject obj) {
            KeyValuePair<string, JsonNode?>[] properties = obj.ToArray();

            foreach (KeyValuePair<string, JsonNode?> property in properties) {
                if (SensitiveDataSanitizer.IsSensitiveName(property.Key)) {
                    obj[property.Key] = JsonValue.Create("***");
                } else if (TryRedactStringValue(property.Value, out JsonNode? redacted)) {
                    obj[property.Key] = redacted;
                } else {
                    RedactNode(property.Value);
                }
            }
        }
    }

    private static bool TryRedactStringValue(JsonNode? node, out JsonNode? redacted) {
        redacted = null;

        if (node is not JsonValue value || !value.TryGetValue(out string? text)) {
            return false;
        }

        redacted = JsonValue.Create(SensitiveDataSanitizer.Redact(text));
        return true;
    }
}
