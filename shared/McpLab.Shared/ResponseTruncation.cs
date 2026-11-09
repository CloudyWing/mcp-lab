using System.Text.Json;
using System.Text.Json.Nodes;
using CloudyWing.McpLab.Shared.Configuration;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Adds the truncation fields to tool response data.
/// </summary>
public static class ResponseTruncation {
    /// <summary>
    /// Serializes <paramref name="data"/> to an object and sets <c>truncated</c>, <c>original_bytes</c>,
    /// <c>original_bytes_exact</c> and <c>max_response_bytes</c> after its existing fields.
    /// </summary>
    /// <param name="data">Response data that serializes to a JSON object.</param>
    /// <param name="originalBytes">Size of the content, or the bytes read before reading stopped.</param>
    /// <param name="originalBytesExact">Whether <paramref name="originalBytes"/> is the exact size.</param>
    /// <param name="maxResponseBytes">The effective response limit.</param>
    public static JsonObject Append(object data, long originalBytes, bool originalBytesExact, int maxResponseBytes) {
        ArgumentNullException.ThrowIfNull(data);

        if (JsonSerializer.SerializeToNode(data, ToolJson.Options) is not JsonObject node) {
            throw new ArgumentException("Response data must serialize to a JSON object.", nameof(data));
        }

        node["truncated"] = true;
        node["original_bytes"] = originalBytes;
        node["original_bytes_exact"] = originalBytesExact;
        node["max_response_bytes"] = maxResponseBytes;

        return node;
    }

    /// <summary>
    /// Adds the truncation fields using the bytes observed by <paramref name="budget"/>, which is never exact.
    /// </summary>
    public static JsonObject Append(object data, ResponseBudget budget) {
        ArgumentNullException.ThrowIfNull(budget);

        return Append(data, budget.ObservedBytes, false, budget.MaxBytes);
    }

    /// <summary>
    /// Adds the truncation fields using the size information of <paramref name="body"/>.
    /// </summary>
    public static JsonObject Append(object data, BoundedBody body, int maxResponseBytes) =>
        Append(data, body.OriginalBytes, body.OriginalBytesExact, maxResponseBytes);

    /// <summary>
    /// Creates the message of a response that was cut at the response limit.
    /// </summary>
    public static string Message(long originalBytes, bool originalBytesExact, int maxResponseBytes) {
        string size = originalBytesExact
            ? $"{originalBytes} bytes"
            : $"at least {originalBytes} bytes";

        return $"The response ({size}) exceeds {ServiceSettings.Common.MaxResponseBytes.Name} ({maxResponseBytes} bytes) "
            + "and was truncated. Increase the setting or narrow the request to receive more.";
    }

    /// <summary>
    /// Creates the <c>data</c> of an error response for content that cannot be returned partially.
    /// </summary>
    public static object ErrorData(long originalBytes, bool originalBytesExact, int maxResponseBytes) =>
        new {
            truncated = true,
            original_bytes = originalBytes,
            original_bytes_exact = originalBytesExact,
            max_response_bytes = maxResponseBytes,
        };
}
