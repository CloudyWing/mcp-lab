using System.Collections.Concurrent;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Redis;

/// <summary>
/// Loads and exposes named Redis connection configurations from environment variables,
/// and keeps one shared multiplexer per configuration.
/// </summary>
public sealed class ConnectionRegistry : IConnectionStatusSource, IDisposable {
    private static readonly TimeSpan RetiredMultiplexerGracePeriod = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<ConnectionConfig, Lazy<Task<IConnectionMultiplexer>>> multiplexers = new();
    private readonly ConnectionRegistryCore<ConnectionConfig> core;

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/>.
    /// </summary>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    /// <param name="logger">The logger.</param>
    public ConnectionRegistry(IDotEnvSnapshotProvider snapshotProvider, ILogger<ConnectionRegistry> logger) {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(logger);

        core = new(ConnectionSchemas.Redis, snapshotProvider, logger, BuildConfig);
        core.Rebuilt += RetireStaleMultiplexers;
    }

    /// <summary>
    /// Gets all loaded connections, keyed by connection name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, ConnectionConfig> All => core.All;

    /// <inheritdoc />
    public ConnectionRegistryStatus Status => core.Status;

    /// <summary>
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty.
    /// </summary>
    public ConnectionConfig Get(string? name) => core.Get(name);

    /// <summary>
    /// Gets the ENVIRONMENT label of a connection, or an empty string.
    /// </summary>
    public string EnvironmentOf(string name) => core.EnvironmentOf(name);

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
        core.Rebuilt -= RetireStaleMultiplexers;
        core.Dispose();

        foreach (ConnectionConfig key in multiplexers.Keys) {
            if (multiplexers.TryRemove(key, out Lazy<Task<IConnectionMultiplexer>>? lazy)) {
                DisposeWhenCompleted(lazy);
            }
        }
    }

    private static async Task<IConnectionMultiplexer> CreateMultiplexerAsync(ConnectionConfig cfg) {
        ConfigurationOptions options = new() {
            EndPoints = { { cfg.Host, cfg.Port } },
            ConnectTimeout = cfg.ConnectTimeoutSeconds * 1000,
            AsyncTimeout = cfg.OperationTimeoutSeconds * 1000,
            SyncTimeout = cfg.OperationTimeoutSeconds * 1000,
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

    private static ConnectionConfig BuildConfig(ConnectionValues values) =>
        new(
            Name: values.Name,
            Host: values.GetString("HOST"),
            Port: values.GetInt("PORT"),
            Password: values.GetString("PASSWORD"),
            Database: values.GetInt("DATABASE"),
            ConnectTimeoutSeconds: values.GetInt("CONNECT_TIMEOUT"),
            OperationTimeoutSeconds: values.GetInt("OPERATION_TIMEOUT")
        );
}
