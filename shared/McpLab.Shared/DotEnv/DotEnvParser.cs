using System.Text;
using System.Text.RegularExpressions;

namespace CloudyWing.McpLab.Shared.DotEnv;

/// <summary>
/// Parses .env file content into key-value pairs.
/// </summary>
public static partial class DotEnvParser {
    /// <summary>
    /// Parses .env content.
    /// </summary>
    /// <param name="content">The .env content.</param>
    /// <returns>The parsed key-value pairs.</returns>
    /// <exception cref="FormatException">A line does not follow the supported .env syntax.</exception>
    public static IReadOnlyDictionary<string, string> Parse(string content) {
        ArgumentNullException.ThrowIfNull(content);

        Dictionary<string, string> result = new(StringComparer.Ordinal);
        using StringReader reader = new(RemoveBom(content));
        int lineNumber = 0;

        while (reader.ReadLine() is string line) {
            lineNumber++;
            ParseLine(line, lineNumber, result);
        }

        return result;
    }

    private static void ParseLine(string line, int lineNumber, Dictionary<string, string> result) {
        string trimmed = line.Trim();

        if (trimmed.Length == 0 || trimmed.StartsWith('#')) {
            return;
        }

        if (trimmed.StartsWith("export ", StringComparison.Ordinal)) {
            trimmed = trimmed[7..].TrimStart();
        }

        int separatorIndex = trimmed.IndexOf('=');

        if (separatorIndex < 0) {
            throw new FormatException($"Invalid .env syntax at line {lineNumber}: missing '='.");
        }

        string key = trimmed[..separatorIndex].Trim();

        if (!EnvironmentKeyRegex().IsMatch(key)) {
            throw new FormatException($"Invalid .env syntax at line {lineNumber}: invalid key '{key}'.");
        }

        string value = trimmed[(separatorIndex + 1)..].Trim();
        result[key] = ParseValue(value, lineNumber);
    }

    private static string ParseValue(string value, int lineNumber) {
        if (value.Length == 0) {
            return "";
        }

        return value[0] switch {
            '"' => ParseDoubleQuotedValue(value, lineNumber),
            '\'' => ParseSingleQuotedValue(value, lineNumber),
            _ => value
        };
    }

    private static string ParseDoubleQuotedValue(string value, int lineNumber) {
        if (value.Length < 2 || value[^1] != '"') {
            throw new FormatException($"Invalid .env syntax at line {lineNumber}: unterminated double-quoted value.");
        }

        StringBuilder builder = new();
        string inner = value[1..^1];

        for (int i = 0; i < inner.Length; i++) {
            char current = inner[i];

            if (current != '\\') {
                builder.Append(current);
                continue;
            }

            if (i == inner.Length - 1) {
                throw new FormatException($"Invalid .env syntax at line {lineNumber}: dangling escape sequence.");
            }

            char escaped = inner[++i];
            builder.Append(escaped switch {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '"' => '"',
                '\\' => '\\',
                _ => escaped
            });
        }

        return builder.ToString();
    }

    private static string ParseSingleQuotedValue(string value, int lineNumber) {
        if (value.Length < 2 || value[^1] != '\'') {
            throw new FormatException($"Invalid .env syntax at line {lineNumber}: unterminated single-quoted value.");
        }

        return value[1..^1];
    }

    private static string RemoveBom(string content) =>
        content.Length > 0 && content[0] == '\uFEFF'
            ? content[1..]
            : content;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex EnvironmentKeyRegex();
}
