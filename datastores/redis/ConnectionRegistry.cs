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
        Dictionary<string, Dictionary<string, string>> buckets = new(StringComparer.OrdinalIgnoreCase);

        foreach (string key in Environment.GetEnvironmentVariables().Keys.OfType<string>()) {
            string val = Environment.GetEnvironmentVariable(key) ?? "";
            Match match = EnvRegex().Match(key);

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

        Dictionary<string, ConnectionConfig> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            string host = (cfg.GetValueOrDefault("host") ?? "").Trim();

            string name = (cfg.GetValueOrDefault("name") ?? "").Trim();

            if (string.IsNullOrEmpty(name)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': REDIS_CONN_{aliasKey}_NAME is required.");
            }

            if (string.IsNullOrEmpty(host)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': REDIS_CONN_{aliasKey}_HOST is required.");
            }

            string portStr = (cfg.GetValueOrDefault("port") ?? "").Trim();
            int port = 6379;

            if (!string.IsNullOrEmpty(portStr)) {
                if (!int.TryParse(portStr, out port) || port <= 0) {
                    throw new InvalidOperationException($"Connection '{aliasKey}': REDIS_CONN_{aliasKey}_PORT must be a valid port number.");
                }
            }

            string password = (cfg.GetValueOrDefault("password") ?? "").Trim();

            if (string.IsNullOrEmpty(password)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': REDIS_CONN_{aliasKey}_PASSWORD is required.");
            }

            string dbStr = (cfg.GetValueOrDefault("database") ?? "").Trim();

            if (string.IsNullOrEmpty(dbStr)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': REDIS_CONN_{aliasKey}_DATABASE is required.");
            }

            if (!int.TryParse(dbStr, out int database) || database < 0) {
                throw new InvalidOperationException($"Connection '{aliasKey}': REDIS_CONN_{aliasKey}_DATABASE must be a valid database number.");
            }

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
