using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Redis;

/// <summary>
/// Loads and exposes named Redis connection configurations from environment variables.
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
            throw new InvalidOperationException("No Redis connections configured. Set REDIS_CONN_<name>_HOST in environment.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return all.Values.First();
        }

        return all.TryGetValue(name, out ConnectionConfig? cfg)
            ? cfg
            : throw new KeyNotFoundException($"Connection '{name}' not found. Available: {string.Join(", ", all.Keys)}");
    }

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

        return new ConnectionConfig(
            Name: name,
            Host: host,
            Port: port,
            Password: password,
            Database: database
        );
    }

    [GeneratedRegex(@"^REDIS_CONN_([A-Z0-9]+)_([A-Z0-9]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();

    [LoggerMessage(
        EventId = 2301,
        Level = LogLevel.Warning,
        Message = "Redis connection '{AliasKey}' rebuild failed: {Reason}; previous configuration retained when available"
    )]
    private static partial void LogConnectionRebuildFailed(ILogger logger, string aliasKey, string reason);
}
