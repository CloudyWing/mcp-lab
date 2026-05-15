using System.Text.Json;

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
            data,
        }, Json);
    }
}
