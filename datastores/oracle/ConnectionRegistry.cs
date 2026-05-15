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
        Dictionary<string, Dictionary<string, string>> buckets =
            EnvironmentConnectionSettings.LoadBuckets(EnvRegex());

        Dictionary<string, ConnectionConfig> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            if (!EnvironmentConnectionSettings.HasConfiguredConnection(
                cfg,
                "name",
                "host",
                "service",
                "user",
                "password"
            )) {
                continue;
            }

            string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "ORACLE_CONN", "name");
            string host = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "ORACLE_CONN", "host");
            string service = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "ORACLE_CONN", "service");
            int port = EnvironmentConnectionSettings.GetOptionalPort(
                cfg,
                aliasKey,
                "ORACLE_CONN",
                1521,
                "port",
                "port"
            );
            string user = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "ORACLE_CONN", "user");
            string password = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "ORACLE_CONN", "password");

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
