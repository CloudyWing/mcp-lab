using System.Text.RegularExpressions;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Loads and validates named connection settings from environment variables.
/// </summary>
public static class EnvironmentConnectionSettings {
    /// <summary>
    /// Loads environment variables that match a connection naming pattern.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> LoadBuckets(Regex envRegex) {
        Dictionary<string, Dictionary<string, string>> buckets = new(StringComparer.OrdinalIgnoreCase);

        foreach (string key in Environment.GetEnvironmentVariables().Keys.OfType<string>()) {
            string val = Environment.GetEnvironmentVariable(key) ?? "";
            Match match = envRegex.Match(key);

            if (!match.Success) {
                continue;
            }

            string aliasKey = match.Groups[1].Value;
            string field = match.Groups[2].Value.ToLowerInvariant();

            if (!buckets.TryGetValue(aliasKey, out Dictionary<string, string>? value)) {
                value = [];
                buckets[aliasKey] = value;
            }

            value[field] = val;
        }

        return buckets;
    }

    /// <summary>
    /// Determines whether a connection bucket contains any configured field.
    /// </summary>
    public static bool HasConfiguredConnection(IReadOnlyDictionary<string, string> cfg, params string[] fields) =>
        fields.Any(field => !string.IsNullOrWhiteSpace(GetString(cfg, field)));

    /// <summary>
    /// Gets a required string field.
    /// </summary>
    public static string GetRequiredString(
        IReadOnlyDictionary<string, string> cfg,
        string aliasKey,
        string envPrefix,
        string field
    ) {
        string value = GetString(cfg, field);

        if (string.IsNullOrEmpty(value)) {
            throw new InvalidOperationException(
                $"Connection '{aliasKey}': {envPrefix}_{aliasKey}_{field.ToUpperInvariant()} is required.");
        }

        return value;
    }

    /// <summary>
    /// Gets an optional TCP port field.
    /// </summary>
    public static int GetOptionalPort(
        IReadOnlyDictionary<string, string> cfg,
        string aliasKey,
        string envPrefix,
        int defaultPort,
        string displayField,
        params string[] fields
    ) {
        string portStr = GetFirstString(cfg, fields);

        if (string.IsNullOrEmpty(portStr)) {
            return defaultPort;
        }

        if (!int.TryParse(portStr, out int port) || port <= 0) {
            throw new InvalidOperationException(
                $"Connection '{aliasKey}': {envPrefix}_{aliasKey}_{displayField.ToUpperInvariant()} " +
                "must be a valid port number.");
        }

        return port;
    }

    /// <summary>
    /// Gets a required non-negative integer field.
    /// </summary>
    public static int GetRequiredNonNegativeInt(
        IReadOnlyDictionary<string, string> cfg,
        string aliasKey,
        string envPrefix,
        string field,
        string valueName
    ) {
        string value = GetRequiredString(cfg, aliasKey, envPrefix, field);

        if (!int.TryParse(value, out int result) || result < 0) {
            throw new InvalidOperationException(
                $"Connection '{aliasKey}': {envPrefix}_{aliasKey}_{field.ToUpperInvariant()} " +
                $"must be a valid {valueName}.");
        }

        return result;
    }

    /// <summary>
    /// Gets a required boolean field.
    /// </summary>
    public static bool GetRequiredBool(
        IReadOnlyDictionary<string, string> cfg,
        string aliasKey,
        string envPrefix,
        string field
    ) {
        string value = GetRequiredString(cfg, aliasKey, envPrefix, field);

        if (!bool.TryParse(value, out bool result)) {
            throw new InvalidOperationException(
                $"Connection '{aliasKey}': {envPrefix}_{aliasKey}_{field.ToUpperInvariant()} must be true or false.");
        }

        return result;
    }

    /// <summary>
    /// Gets a string field without required validation.
    /// </summary>
    public static string GetString(IReadOnlyDictionary<string, string> cfg, string field) =>
        (cfg.GetValueOrDefault(field.ToLowerInvariant()) ?? "").Trim();

    private static string GetFirstString(IReadOnlyDictionary<string, string> cfg, IEnumerable<string> fields) {
        foreach (string field in fields) {
            string value = GetString(cfg, field);

            if (!string.IsNullOrEmpty(value)) {
                return value;
            }
        }

        return "";
    }
}
