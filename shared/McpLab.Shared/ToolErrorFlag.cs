using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Protocol;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Marks tool results whose <see cref="ToolResponse"/> envelope reports a failure with the MCP <c>isError</c> flag,
/// so clients can tell failures apart without parsing the JSON text.
/// </summary>
public static class ToolErrorFlag {
    /// <summary>
    /// Adds a call-tool filter that sets <c>isError</c> when the envelope has <c>ok=false</c>.
    /// </summary>
    public static IMcpServerBuilder WithToolErrorFlag(this IMcpServerBuilder builder) =>
        builder.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) => {
            CallToolResult result = await next(context, cancellationToken).ConfigureAwait(false);

            if (result.IsError != true && result.Content is [TextContentBlock { Text: string text }] && IsFailureEnvelope(text)) {
                result.IsError = true;
            }

            return result;
        }));

    /// <summary>
    /// Returns whether <paramref name="text"/> is a tool response envelope with <c>ok=false</c>.
    /// </summary>
    public static bool IsFailureEnvelope(string text) {
        if (!text.StartsWith('{')) {
            return false;
        }

        try {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;

            return root.TryGetProperty("ok", out JsonElement ok)
                && ok.ValueKind == JsonValueKind.False
                && root.TryGetProperty("kind", out _);
        } catch (JsonException) {
            return false;
        }
    }
}
