using System.Text.RegularExpressions;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Redacts credentials from tool responses and diagnostic messages.
/// </summary>
public static partial class SensitiveDataSanitizer {
    /// <summary>
    /// Redacts likely credentials from the supplied value.
    /// </summary>
    public static string Redact(string? value) {
        if (string.IsNullOrEmpty(value)) {
            return "";
        }

        string redacted = UrlCredentialsRegex().Replace(value, "$1***:***@");
        redacted = KeyValueSecretRegex().Replace(redacted, "$1$2***");
        redacted = JsonSecretRegex().Replace(redacted, "$1***$3");

        return redacted;
    }

    /// <summary>
    /// Determines whether a field name is likely to contain a credential.
    /// </summary>
    public static bool IsSensitiveName(string name) => SensitiveNameRegex().IsMatch(name);

    [GeneratedRegex(@"(://)[^:/@\s]+:[^/@\s]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentialsRegex();

    [GeneratedRegex(@"(?i)(password|pwd|token|secret|api[_-]?key|access[_-]?key)")]
    private static partial Regex SensitiveNameRegex();

    [GeneratedRegex(
        @"(?i)\b(password|pwd|token|secret|api[_-]?key|access[_-]?key)\b(\s*=\s*|:\s*)[^;,\s}]+",
        RegexOptions.Singleline
    )]
    private static partial Regex KeyValueSecretRegex();

    [GeneratedRegex(
        @"(?i)(""[^""]*(password|pwd|token|secret|api[_-]?key|access[_-]?key)[^""]*""\s*:\s*"")[^""]*("")",
        RegexOptions.Singleline
    )]
    private static partial Regex JsonSecretRegex();
}
