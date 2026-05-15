using System.Text;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Decodes UTF-8 Base64 payloads used by MCP tools.
/// </summary>
public static class Base64Payload {
    /// <summary>
    /// Decodes a Base64 or URL-safe Base64 payload into a UTF-8 string.
    /// </summary>
    public static bool TryDecodeUtf8(
        string input,
        string emptyPayloadError,
        string decodedEmptyError,
        out string value,
        out string error
    ) {
        value = "";
        error = "";

        if (string.IsNullOrWhiteSpace(input)) {
            error = emptyPayloadError;
            return false;
        }

        try {
            string normalized = Normalize(input);
            byte[] bytes = Convert.FromBase64String(normalized);
            value = Encoding.UTF8.GetString(bytes);

            if (string.IsNullOrWhiteSpace(value)) {
                error = decodedEmptyError;
                return false;
            }

            return true;
        } catch (FormatException) {
            error = "Invalid Base64.";
            return false;
        }
    }

    private static string Normalize(string input) {
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
