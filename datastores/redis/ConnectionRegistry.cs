using System.Collections.Concurrent;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Redis;

/// <summary>
/// Loads and exposes named Redis connection configurations from environment variables,
/// and keeps one shared multiplexer per configuration.
/// </summary>
public sealed partial class ConnectionRegistry : IDisposable {
    private static readonly TimeSpan RetiredMultiplexerGracePeriod = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<ConnectionConfig, Lazy<Task<IConnectionMultiplexer>>> multiplexers = new();
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
    /// Returns the shared <see cref="IConnectionMultiplexer"/> for the specified named connection.
    /// Callers must not dispose it; the registry replaces it when the connection settings change.
    /// </summary>
    public async Task<IConnectionMultiplexer> ConnectAsync(string? connectionName) {
        ConnectionConfig cfg = Get(connectionName);
        Lazy<Task<IConnectionMultiplexer>> lazy = multiplexers.GetOrAdd(
            cfg,
            key => new Lazy<Task<IConnectionMultiplexer>>(() => CreateMultiplexerAsync(key))
        );

        try {
            return await lazy.Value.ConfigureAwait(false);
        } catch {
            multiplexers.TryRemove(new KeyValuePair<ConnectionConfig, Lazy<Task<IConnectionMultiplexer>>>(cfg, lazy));
            throw;
        }
    }

    /// <summary>
    /// Gets the number of cached multiplexers.
    /// </summary>
    public int CachedMultiplexerCount => multiplexers.Count;

    /// <inheritdoc />
    public void Dispose() {
        snapshotProvider.Reloaded -= HandleDotEnvReloaded;

        foreach (ConnectionConfig key in multiplexers.Keys) {
            if (multiplexers.TryRemove(key, out Lazy<Task<IConnectionMultiplexer>>? lazy)) {
                DisposeWhenCompleted(lazy);
            }
        }
    }

    private static async Task<IConnectionMultiplexer> CreateMultiplexerAsync(ConnectionConfig cfg) {
        ConfigurationOptions options = new() {
            EndPoints = { { cfg.Host, cfg.Port } },
            ConnectTimeout = 5000,
            AsyncTimeout = 10000,
            DefaultDatabase = cfg.Database,
        };

        if (!string.IsNullOrEmpty(cfg.Password)) {
            options.Password = cfg.Password;
        }

        return await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
    }

    private void RetireStaleMultiplexers(IReadOnlyDictionary<string, ConnectionConfig> active) {
        HashSet<ConnectionConfig> activeConfigs = [.. active.Values];

        foreach (ConnectionConfig key in multiplexers.Keys) {
            if (activeConfigs.Contains(key) || !multiplexers.TryRemove(key, out Lazy<Task<IConnectionMultiplexer>>? lazy)) {
                continue;
            }

            // Requests that already hold the old multiplexer get time to finish before it is closed.
            _ = Task.Delay(RetiredMultiplexerGracePeriod).ContinueWith(
                _ => DisposeWhenCompleted(lazy),
                TaskScheduler.Default
            );
        }
    }

    private static void DisposeWhenCompleted(Lazy<Task<IConnectionMultiplexer>> lazy) {
        if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully) {
            lazy.Value.Result.Dispose();
        }
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
        RetireStaleMultiplexers(result);
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
