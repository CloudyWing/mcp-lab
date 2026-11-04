using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.RabbitMq;

/// <summary>
/// Loads and exposes named RabbitMQ connection configurations from environment variables.
/// </summary>
public sealed class ConnectionRegistry : IConnectionStatusSource, IDisposable {
    /// <summary>
    /// Named HTTP client key used for RabbitMQ Management API requests.
    /// </summary>
    public const string ClientKey = "rabbitmq";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly ConnectionRegistryCore<ConnectionConfig> core;
    private readonly IDotEnvSnapshotProvider snapshotProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/>.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    /// <param name="logger">The logger.</param>
    public ConnectionRegistry(
        IHttpClientFactory httpClientFactory,
        IDotEnvSnapshotProvider snapshotProvider,
        ILogger<ConnectionRegistry> logger
    ) {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClientFactory = httpClientFactory;
        this.snapshotProvider = snapshotProvider;
        core = new(ConnectionSchemas.RabbitMq, snapshotProvider, logger, BuildConfig);
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
    /// Creates and configures an <see cref="HttpClient"/> for the specified named RabbitMQ Management API connection.
    /// </summary>
    public HttpClient CreateClient(string? connectionName) {
        ConnectionConfig cfg = Get(connectionName);
        HttpClient http = httpClientFactory.CreateClient(ClientKey);
        http.Timeout = ServiceSettings.RabbitMq.HttpTimeout.ReadSeconds(snapshotProvider.Current);
        http.BaseAddress = new Uri($"http://{cfg.Host}:{cfg.MgmtPort}/");

        string creds = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{cfg.User}:{cfg.Password}"));

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", creds);
        http.DefaultRequestHeaders.Add("Accept", "application/json");
        return http;
    }

    /// <inheritdoc />
    public void Dispose() => core.Dispose();

    private static ConnectionConfig BuildConfig(ConnectionValues values) =>
        new(
            Name: values.Name,
            Host: values.GetString("HOST"),
            MgmtPort: values.GetInt("MGMTPORT"),
            User: values.GetString("USER"),
            Password: values.GetString("PASSWORD")
        );
}
