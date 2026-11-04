using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.SqlServer;

/// <summary>
/// Loads and exposes named SQL Server connection configurations from environment variables.
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

        core = new(ConnectionSchemas.SqlServer, snapshotProvider, logger, BuildConfig);
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
    /// Opens and returns a new <see cref="SqlConnection"/> for the specified named connection.
    /// </summary>
    public async Task<SqlConnection> OpenAsync(string? connectionName, string? database, CancellationToken cancellationToken) {
        ConnectionConfig cfg = Get(connectionName);
        string db = !string.IsNullOrWhiteSpace(database) ? database : cfg.Database;
        string cs = new SqlConnectionStringBuilder {
            DataSource = $"{cfg.Host},{cfg.Port}",
            UserID = cfg.User,
            Password = cfg.Password,
            InitialCatalog = db,
            TrustServerCertificate = cfg.TrustServerCertificate,
            ConnectTimeout = cfg.ConnectTimeoutSeconds,
        }.ConnectionString;
        SqlConnection conn = new(cs);

        try {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        } catch {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return conn;
    }

    /// <inheritdoc />
    public void Dispose() => core.Dispose();

    private static ConnectionConfig BuildConfig(ConnectionValues values) =>
        new(
            Name: values.Name,
            Host: values.GetString("HOST"),
            Port: values.GetInt("PORT"),
            User: values.GetString("USER"),
            Password: values.GetString("PASSWORD"),
            Database: values.GetString("DATABASE"),
            TrustServerCertificate: values.GetBool("TRUST_SERVER_CERTIFICATE"),
            ConnectTimeoutSeconds: values.GetInt("CONNECT_TIMEOUT")
        );
}
