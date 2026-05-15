namespace CloudyWing.McpLab.SqlServer;

/// <summary>
/// Loads and exposes named SQL Server connection configurations from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    /// <summary>
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty; throws <see cref="KeyNotFoundException"/> when not found.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        if (All.Count == 0) {
            throw new InvalidOperationException("No SQL Server connections configured. Set MSSQL_CONN_<name>_HOST in environment.");
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
    /// Opens and returns a new <see cref="SqlConnection"/> for the specified named connection.
    /// </summary>
    public SqlConnection Open(string? connectionName, string? database = null) {
        ConnectionConfig cfg = Get(connectionName);
        string db = !string.IsNullOrWhiteSpace(database) ? database : cfg.Database;
        string cs = new SqlConnectionStringBuilder {
            DataSource = $"{cfg.Host},{cfg.Port}",
            UserID = cfg.User,
            Password = cfg.Password,
            InitialCatalog = db,
            TrustServerCertificate = true,
            ConnectTimeout = 30,
        }.ConnectionString;
        SqlConnection conn = new(cs);
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
                "user",
                "password",
                "database"
            )) {
                continue;
            }

            string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MSSQL_CONN", "name");
            string host = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MSSQL_CONN", "host");
            int port = EnvironmentConnectionSettings.GetOptionalPort(cfg, aliasKey, "MSSQL_CONN", 1433, "port", "port");
            string user = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MSSQL_CONN", "user");
            string password = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MSSQL_CONN", "password");
            string database = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MSSQL_CONN", "database");

            result[name] = new ConnectionConfig(
                Name: name,
                Host: host,
                Port: port,
                User: user,
                Password: password,
                Database: database);
        }

        return result;
    }

    [GeneratedRegex(@"^MSSQL_CONN_([A-Z0-9]+)_([A-Z0-9]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();
}
