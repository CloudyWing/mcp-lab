namespace CloudyWing.McpLab.Oracle;

/// <summary>
/// Loads and exposes named Oracle connection configurations from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    /// <summary>
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty; throws <see cref="KeyNotFoundException"/> when not found.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        if (All.Count == 0) {
            throw new InvalidOperationException(
                "No Oracle connections configured. Set ORACLE_CONN_<name>_HOST and ORACLE_CONN_<name>_SERVICE in environment.");
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
    /// Opens and returns a new <see cref="OracleConnection"/> for the specified named connection.
    /// </summary>
    public OracleConnection Open(string? connectionName) {
        ConnectionConfig cfg = Get(connectionName);
        string cs = $"Data Source={cfg.Host}:{cfg.Port}/{cfg.Service};User Id={cfg.User};Password={cfg.Password};";
        OracleConnection conn = new(cs);
        conn.Open();
        return conn;
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
            string service = (cfg.GetValueOrDefault("service") ?? "").Trim();

            string name = (cfg.GetValueOrDefault("name") ?? "").Trim();

            if (string.IsNullOrEmpty(name)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ORACLE_CONN_{aliasKey}_NAME is required.");
            }

            if (string.IsNullOrEmpty(host)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ORACLE_CONN_{aliasKey}_HOST is required.");
            }

            if (string.IsNullOrEmpty(service)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ORACLE_CONN_{aliasKey}_SERVICE is required.");
            }

            string portStr = (cfg.GetValueOrDefault("port") ?? "").Trim();
            int port = 1521;

            if (!string.IsNullOrEmpty(portStr)) {
                if (!int.TryParse(portStr, out port) || port <= 0) {
                    throw new InvalidOperationException($"Connection '{aliasKey}': ORACLE_CONN_{aliasKey}_PORT must be a valid port number.");
                }
            }

            string user = (cfg.GetValueOrDefault("user") ?? "").Trim();

            if (string.IsNullOrEmpty(user)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ORACLE_CONN_{aliasKey}_USER is required.");
            }

            string password = (cfg.GetValueOrDefault("password") ?? "").Trim();

            if (string.IsNullOrEmpty(password)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ORACLE_CONN_{aliasKey}_PASSWORD is required.");
            }

            result[name] = new ConnectionConfig(
                Name: name,
                Host: host,
                Port: port,
                Service: service,
                User: user,
                Password: password
            );
        }

        return result;
    }

    [GeneratedRegex(@"^ORACLE_CONN_([A-Z0-9]+)_([A-Z0-9]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();
}
