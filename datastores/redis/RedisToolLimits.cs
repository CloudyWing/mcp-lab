namespace CloudyWing.McpLab.Redis;

/// <summary>
/// Provides bounded input rules for Redis MCP tools.
/// </summary>
public static class RedisToolLimits {
    private const int DefaultKeyCount = 100;
    private const int MaxKeyCount = 1000;

    /// <summary>
    /// Normalizes the requested Redis key enumeration count.
    /// </summary>
    public static int NormalizeKeyCount(int count) =>
        ToolRuntimeOptions.NormalizeRequestedInt32(count, DefaultKeyCount, 1, MaxKeyCount);

    /// <summary>
    /// Returns whether a string SET TTL is valid.
    /// </summary>
    public static bool IsSetKeyTtlValid(int ttlSeconds) => ttlSeconds >= 0;

    /// <summary>
    /// Returns whether an explicit key expiration TTL is valid.
    /// </summary>
    public static bool IsExpireTtlValid(int ttlSeconds) => ttlSeconds > 0;
}
