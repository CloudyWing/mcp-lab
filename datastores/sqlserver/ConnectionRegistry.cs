using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.SqlServer;

/// <summary>
/// Loads and exposes named SQL Server connection configurations from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    private readonly IDotEnvSnapshotProvider snapshotProvider;
    private readonly ILogger<ConnectionRegistry> logger;
    private IReadOnlyDictionary<string, ConnectionConfig> current =
        new Dictionary<string, ConnectionConfig>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/>.
    /// </summary>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    /// <param name="logger">The logger.</param>
    public ConnectionRegistry(IDotEnvSnapshotProvider snapshotProvider, ILogger<ConnectionRegistry> logger) {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.snapshotProvider = snapshotProvider;
        this.logger = logger;
        Rebuild(snapshotProvider.Current);
        this.snapshotProvider.Reloaded += HandleDotEnvReloaded;
    }

    /// <summary>
    /// Gets all loaded connections, keyed by connection name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, ConnectionConfig> All => Volatile.Read(ref current);

    /// <summary>
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty; throws <see cref="KeyNotFoundException"/> when not found.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        IReadOnlyDictionary<string, ConnectionConfig> all = All;

        if (all.Count == 0) {
            throw new InvalidOperationException("No SQL Server connections configured. Set MSSQL_CONN_<name>_HOST in environment.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return all.Values.First();
        }

        return all.TryGetValue(name, out ConnectionConfig? cfg)
            ? cfg
            : throw new KeyNotFoundException($"Connection '{name}' not found. Available: {string.Join(", ", all.Keys)}");
    }

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

    private void HandleDotEnvReloaded(object? sender, DotEnvReloadedEventArgs e) {
        Rebuild(e.Current);
    }

    private void Rebuild(IReadOnlyDictionary<string, string> snapshot) {
        IReadOnlyDictionary<string, ConnectionConfig> previous = All;
        Dictionary<string, Dictionary<string, string>> buckets =
            EnvironmentConnectionSettings.LoadBuckets(snapshot, EnvRegex());

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

            try {
                ConnectionConfig config = BuildConfig(aliasKey, cfg);
                result[config.Name] = config;
            } catch (InvalidOperationException ex) {
                string configuredName = EnvironmentConnectionSettings.GetString(cfg, "name");

                if (!string.IsNullOrWhiteSpace(configuredName) && previous.TryGetValue(configuredName, out ConnectionConfig? previousConfig)) {
                    result[configuredName] = previousConfig;
                }

                LogConnectionRebuildFailed(logger, aliasKey, ex.Message);
            }
        }

        Volatile.Write(ref current, result);
    }

    private static ConnectionConfig BuildConfig(string aliasKey, IReadOnlyDictionary<string, string> cfg) {
        string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MSSQL_CONN", "name");
        string host = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MSSQL_CONN", "host");
        int port = EnvironmentConnectionSettings.GetOptionalPort(cfg, aliasKey, "MSSQL_CONN", 1433, "port", "port");
        string user = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MSSQL_CONN", "user");
        string password = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MSSQL_CONN", "password");
        string database = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MSSQL_CONN", "database");

        return new ConnectionConfig(
            Name: name,
            Host: host,
            Port: port,
            User: user,
            Password: password,
            Database: database);
    }

    [GeneratedRegex(@"^MSSQL_CONN_([A-Z0-9]+)_([A-Z0-9]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message = "SQL Server connection '{AliasKey}' rebuild failed: {Reason}; previous configuration retained when available"
    )]
    private static partial void LogConnectionRebuildFailed(ILogger logger, string aliasKey, string reason);
}
