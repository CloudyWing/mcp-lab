using CloudyWing.McpLab.Shared.Configuration;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Elastic;

/// <summary>
/// Loads and exposes named Elasticsearch connection configurations from environment variables.
/// </summary>
public sealed class ConnectionRegistry : IConnectionStatusSource, IDisposable {
    /// <summary>
    /// Named HTTP client key used for connections with default TLS validation.
    /// </summary>
    public const string PlainClientKey = "elastic";

    /// <summary>
    /// Named HTTP client key used for connections that skip TLS certificate validation.
    /// </summary>
    public const string SslSkipClientKey = "elastic-ssl-skip";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly ConnectionRegistryCore<ConnectionConfig> core;

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
        core = new(ConnectionSchemas.Elasticsearch, snapshotProvider, logger, BuildConfig);
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
    /// Creates and configures an <see cref="HttpClient"/> for the specified named Elasticsearch connection.
    /// </summary>
    public HttpClient CreateClient(string? connectionName) {
        ConnectionConfig cfg = Get(connectionName);
        string clientKey = cfg.SslSkipVerify ? SslSkipClientKey : PlainClientKey;
        HttpClient http = httpClientFactory.CreateClient(clientKey);

        http.BaseAddress = new Uri(cfg.Url.TrimEnd('/') + "/");

        if (!string.IsNullOrEmpty(cfg.User)) {
            string creds = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{cfg.User}:{cfg.Password}")
            );
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", creds);
        }

        http.DefaultRequestHeaders.Add("Accept", "application/json");
        return http;
    }

    /// <inheritdoc />
    public void Dispose() => core.Dispose();

    private static ConnectionConfig BuildConfig(ConnectionValues values) =>
        new(
            Name: values.Name,
            Url: values.GetString("URL"),
            User: values.GetString("USER"),
            Password: values.GetString("PASSWORD"),
            SslSkipVerify: values.GetBool("SSL_SKIP_VERIFY")
        );
}
