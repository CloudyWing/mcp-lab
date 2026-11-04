using System.Text;
using System.Text.RegularExpressions;
using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// An editable .env file that keeps comments, blank lines, and key order when values change.
/// </summary>
public sealed partial class EnvFile {
    private readonly List<string> lines;

    private EnvFile(IEnumerable<string> lines) {
        this.lines = [.. lines];
    }

    /// <summary>
    /// Parses file content. Throws <see cref="FormatException"/> when the content is not valid .env syntax.
    /// </summary>
    public static EnvFile Parse(string content) {
        ArgumentNullException.ThrowIfNull(content);
        DotEnvParser.Parse(content);

        string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

        return new EnvFile(normalized.Length == 0 ? [] : normalized.Split('\n'));
    }

    /// <summary>
    /// Gets the parsed key-value pairs.
    /// </summary>
    public IReadOnlyDictionary<string, string> Values => DotEnvParser.Parse(ToString());

    /// <summary>
    /// Sets a key, replacing its first occurrence and removing duplicates, or appending it at the end.
    /// </summary>
    public void Set(string key, string value) {
        ValidateKey(key);
        string line = $"{key}={Quote(value)}";
        int index = FindIndex(key);

        if (index < 0) {
            lines.Add(line);
            return;
        }

        lines[index] = line;
        RemoveWhere(i => i > index && KeyOf(lines[i]) == key);
    }

    /// <summary>
    /// Removes every line that assigns <paramref name="key"/>.
    /// </summary>
    public void Remove(string key) {
        RemoveWhere(i => KeyOf(lines[i]) == key);
    }

    /// <summary>
    /// Removes every line whose key starts with <paramref name="prefix"/>.
    /// </summary>
    public void RemovePrefix(string prefix) {
        RemoveWhere(i => KeyOf(lines[i])?.StartsWith(prefix, StringComparison.Ordinal) == true);
    }

    /// <inheritdoc />
    public override string ToString() => lines.Count == 0 ? "" : string.Join("\n", lines) + "\n";

    /// <summary>
    /// Formats a value so <see cref="DotEnvParser"/> reads it back unchanged.
    /// </summary>
    public static string Quote(string value) {
        ArgumentNullException.ThrowIfNull(value);

        bool needsQuotes = value.Length > 0 && (
            value != value.Trim()
            || value[0] is '"' or '\''
            || value.Contains('\n') || value.Contains('\r')
        );

        if (!needsQuotes) {
            return value;
        }

        StringBuilder builder = new("\"");

        foreach (char current in value) {
            builder.Append(current switch {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => current.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    private int FindIndex(string key) => lines.FindIndex(line => KeyOf(line) == key);

    private void RemoveWhere(Func<int, bool> predicate) {
        for (int i = lines.Count - 1; i >= 0; i--) {
            if (predicate(i)) {
                lines.RemoveAt(i);
            }
        }
    }

    private static string? KeyOf(string line) {
        Match match = KeyLineRegex().Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void ValidateKey(string key) {
        if (!KeyRegex().IsMatch(key)) {
            throw new ArgumentException($"Invalid variable name '{key}'.", nameof(key));
        }
    }

    [GeneratedRegex(@"^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=")]
    private static partial Regex KeyLineRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex KeyRegex();
}
