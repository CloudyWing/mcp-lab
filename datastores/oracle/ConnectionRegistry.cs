using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Oracle;

/// <summary>
/// Loads and exposes named Oracle connection configurations from environment variables.
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
            throw new InvalidOperationException(
                "No Oracle connections configured. Set ORACLE_CONN_<name>_HOST and ORACLE_CONN_<name>_SERVICE in environment.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return all.Values.First();
        }

        return all.TryGetValue(name, out ConnectionConfig? cfg)
            ? cfg
            : throw new KeyNotFoundException($"Connection '{name}' not found. Available: {string.Join(", ", all.Keys)}");
    }

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
                "service",
                "user",
                "password"
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

        return new ConnectionConfig(
            Name: name,
            Host: host,
            Port: port,
            Service: service,
            User: user,
            Password: password
        );
    }

    [GeneratedRegex(@"^ORACLE_CONN_([A-Z0-9]+)_([A-Z0-9]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Warning,
        Message = "Oracle connection '{AliasKey}' rebuild failed: {Reason}; previous configuration retained when available"
    )]
    private static partial void LogConnectionRebuildFailed(ILogger logger, string aliasKey, string reason);
}
