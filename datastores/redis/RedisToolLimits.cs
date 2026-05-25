using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.Redis;

/// <summary>
/// Provides bounded input rules for Redis MCP tools.
/// </summary>
public sealed class RedisToolLimits {
    private const int DefaultKeyCount = 100;
    private const int MaxKeyCount = 1000;
    private readonly IDotEnvSnapshotProvider snapshotProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="RedisToolLimits"/>.
    /// </summary>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    public RedisToolLimits(IDotEnvSnapshotProvider snapshotProvider) {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        this.snapshotProvider = snapshotProvider;
    }

    /// <summary>
    /// Gets the configured default key count.
    /// </summary>
    public int KeyCount =>
        ToolRuntimeOptions.GetSnapshotInt32(
            snapshotProvider.Current,
            ["REDIS_MAX_ROWS", "MAX_ROWS"],
            DefaultKeyCount,
            1,
            MaxKeyCount
        );

    /// <summary>
    /// Normalizes the requested Redis key enumeration count.
    /// </summary>
    public int NormalizeKeyCount(int count) =>
        ToolRuntimeOptions.NormalizeRequestedInt32(count, KeyCount, 1, MaxKeyCount);

    /// <summary>
    /// Returns whether a string SET TTL is valid.
    /// </summary>
    public bool IsSetKeyTtlValid(int ttlSeconds) => ttlSeconds >= 0;

    /// <summary>
    /// Returns whether an explicit key expiration TTL is valid.
    /// </summary>
    public bool IsExpireTtlValid(int ttlSeconds) => ttlSeconds > 0;
}
