namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Provides bounded runtime option parsing for MCP tools.
/// </summary>
public static class ToolRuntimeOptions {
    /// <summary>
    /// Gets a bounded integer value from an environment variable.
    /// </summary>
    public static int GetEnvironmentInt32(
        string name,
        int defaultValue,
        int minValue,
        int maxValue
    ) {
        return GetEnvironmentInt32([name], defaultValue, minValue, maxValue);
    }

    /// <summary>
    /// Gets a bounded integer value from the first configured environment variable.
    /// </summary>
    public static int GetEnvironmentInt32(
        IEnumerable<string> names,
        int defaultValue,
        int minValue,
        int maxValue
    ) {
        ValidateBounds(defaultValue, minValue, maxValue);

        foreach (string name in names) {
            string? value = Environment.GetEnvironmentVariable(name);

            if (!string.IsNullOrWhiteSpace(value)) {
                return NormalizeInt32(value, defaultValue, minValue, maxValue);
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// Normalizes a string integer value with a fallback and upper bound.
    /// </summary>
    public static int NormalizeInt32(
        string? value,
        int defaultValue,
        int minValue,
        int maxValue
    ) {
        ValidateBounds(defaultValue, minValue, maxValue);

        if (!int.TryParse(value, out int parsed) || parsed < minValue) {
            return defaultValue;
        }

        return parsed > maxValue ? maxValue : parsed;
    }

    /// <summary>
    /// Normalizes a caller-supplied positive limit with a fallback and upper bound.
    /// </summary>
    public static int NormalizeRequestedInt32(
        int value,
        int defaultValue,
        int minValue,
        int maxValue
    ) {
        ValidateBounds(defaultValue, minValue, maxValue);

        if (value < minValue) {
            return defaultValue;
        }

        return value > maxValue ? maxValue : value;
    }

    private static void ValidateBounds(int defaultValue, int minValue, int maxValue) {
        if (minValue > maxValue) {
            throw new ArgumentException("Minimum value must be less than or equal to maximum value.", nameof(minValue));
        }

        if (defaultValue < minValue || defaultValue > maxValue) {
            throw new ArgumentOutOfRangeException(
                nameof(defaultValue),
                "Default value must be within the configured bounds."
            );
        }
    }
}
