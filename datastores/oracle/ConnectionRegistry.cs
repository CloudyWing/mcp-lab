using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Oracle;

/// <summary>
/// Loads and exposes named Oracle connection configurations from environment variables.
/// </summary>
public sealed class ConnectionRegistry : IConnectionStatusSource, IDisposable {
    private readonly ConnectionRegistryCore<ConnectionConfig> core;

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/>.
    /// </summary>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    /// <param name="logger">The logger.</param>
    public ConnectionRegistry(IDotEnvSnapshotProvider snapshotProvider, ILogger<ConnectionRegistry> logger) {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(logger);

        core = new(ConnectionSchemas.Oracle, snapshotProvider, logger, BuildConfig);
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
    /// Opens and returns a new <see cref="OracleConnection"/> for the specified named connection.
    /// </summary>
    public OracleConnection Open(string? connectionName) {
        ConnectionConfig cfg = Get(connectionName);
        string cs = $"Data Source={cfg.Host}:{cfg.Port}/{cfg.Service};User Id={cfg.User};Password={cfg.Password};";
        OracleConnection conn = new(cs);
        conn.Open();
        return conn;
    }

    /// <inheritdoc />
    public void Dispose() => core.Dispose();

    private static ConnectionConfig BuildConfig(ConnectionValues values) =>
        new(
            Name: values.Name,
            Host: values.GetString("HOST"),
            Port: values.GetInt("PORT"),
            Service: values.GetString("SERVICE"),
            User: values.GetString("USER"),
            Password: values.GetString("PASSWORD")
        );
}
