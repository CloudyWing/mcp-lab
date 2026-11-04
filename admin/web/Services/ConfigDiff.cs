namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// One changed variable between two versions. Values of credential variables are never included.
/// </summary>
/// <param name="Key">Variable name.</param>
/// <param name="Change"><c>added</c>, <c>removed</c>, or <c>changed</c>.</param>
/// <param name="OldValue">Previous value, or <see langword="null"/> when hidden or absent.</param>
/// <param name="NewValue">New value, or <see langword="null"/> when hidden or absent.</param>
/// <param name="IsSecret">Whether the values are hidden because the variable is a credential.</param>
public sealed record ConfigChange(string Key, string Change, string? OldValue, string? NewValue, bool IsSecret);

/// <summary>
/// Compares two config file versions by variable.
/// </summary>
public static class ConfigDiff {
    /// <summary>
    /// Lists variables that differ, in key order. Unreadable content counts as empty.
    /// </summary>
    public static IReadOnlyList<ConfigChange> Compare(string oldContent, string newContent) {
        IReadOnlyDictionary<string, string> before = Read(oldContent);
        IReadOnlyDictionary<string, string> after = Read(newContent);
        List<ConfigChange> changes = [];

        foreach (string key in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal)) {
            bool hadValue = before.TryGetValue(key, out string? oldValue);
            bool hasValue = after.TryGetValue(key, out string? newValue);

            if (hadValue && hasValue && oldValue == newValue) {
                continue;
            }

            bool secret = SensitiveDataSanitizer.IsSensitiveName(key);
            string change = !hadValue ? "added" : !hasValue ? "removed" : "changed";
            changes.Add(new ConfigChange(key, change, secret ? null : oldValue, secret ? null : newValue, secret));
        }

        return changes;
    }

    private static IReadOnlyDictionary<string, string> Read(string content) {
        try {
            return EnvFile.Parse(content).Values;
        } catch (FormatException) {
            return new Dictionary<string, string>();
        }
    }
}
