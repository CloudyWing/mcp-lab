using System.Text.Encodings.Web;
using System.Text.Json;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Provides the JSON serializer options shared by all tool responses.
/// </summary>
/// <remarks>
/// The options use <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>, so non-ASCII characters and
/// HTML-sensitive characters such as <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>'</c>, <c>+</c> and the backtick are
/// written as-is instead of <c>\uXXXX</c>. This is only safe because tool responses are MCP text content that is
/// never embedded in HTML. Quotes, backslashes and control characters are still escaped.
/// </remarks>
public static class ToolJson {
    /// <summary>
    /// Gets the single options instance used to serialize tool response envelopes, data and budgeted items.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new() {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
