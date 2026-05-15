namespace CloudyWing.McpLab.Redis;

/// <summary>
/// Loads and exposes named Redis connection configurations from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    /// <summary>
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty; throws <see cref="KeyNotFoundException"/> when not found.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        if (All.Count == 0) {
            throw new InvalidOperationException("No Redis connections configured. Set REDIS_CONN_<name>_HOST in environment.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return All.Values.First();
        }

        return All.TryGetValue(name, out ConnectionConfig? cfg)
            ? cfg
            : throw new KeyNotFoundException($"Connection '{name}' not found. Available: {string.Join(", ", All.Keys)}");
    }

    /// <summary>
    /// Gets all loaded connections, keyed by connection name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, ConnectionConfig> All { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/> by loading environment variables.
    /// </summary>
    public ConnectionRegistry() => All = Load();

    /// <summary>
    /// Connects to Redis and returns a connected <see cref="IConnectionMultiplexer"/> for the specified named connection.
    /// </summary>
    public async Task<IConnectionMultiplexer> ConnectAsync(string? connectionName) {
        ConnectionConfig cfg = Get(connectionName);
        ConfigurationOptions options = new() {
            EndPoints = { { cfg.Host, cfg.Port } },
            ConnectTimeout = 5000,
            AsyncTimeout = 10000,
            DefaultDatabase = cfg.Database,
        };

        if (!string.IsNullOrEmpty(cfg.Password)) {
            options.Password = cfg.Password;
        }

        return await ConnectionMultiplexer.ConnectAsync(options);
    }

    private static Dictionary<string, ConnectionConfig> Load() {
        Dictionary<string, Dictionary<string, string>> buckets =
            EnvironmentConnectionSettings.LoadBuckets(EnvRegex());

        Dictionary<string, ConnectionConfig> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            if (!EnvironmentConnectionSettings.HasConfiguredConnection(
                cfg,
                "name",
                "host",
                "password",
                "database"
            )) {
                continue;
            }

            string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "REDIS_CONN", "name");
            string host = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "REDIS_CONN", "host");
            int port = EnvironmentConnectionSettings.GetOptionalPort(cfg, aliasKey, "REDIS_CONN", 6379, "port", "port");
            string password = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "REDIS_CONN", "password");
            int database = EnvironmentConnectionSettings.GetRequiredNonNegativeInt(
                cfg,
                aliasKey,
                "REDIS_CONN",
                "database",
                "database number"
            );

            result[name] = new ConnectionConfig(
                Name: name,
                Host: host,
                Port: port,
                Password: password,
                Database: database
            );
        }

        return result;
    }

    [GeneratedRegex(@"^REDIS_CONN_([A-Z0-9]+)_([A-Z0-9]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();
}
