using CloudyWing.McpLab.Shared.Configuration;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Mailpit;

/// <summary>
/// Loads and exposes named Mailpit connection configurations from environment variables.
/// </summary>
public sealed class ConnectionRegistry : IConnectionStatusSource, IDisposable {
    /// <summary>
    /// Named HTTP client key used for Mailpit API calls.
    /// </summary>
    public const string ClientKey = "mailpit";

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
        core = new(ConnectionSchemas.Mailpit, snapshotProvider, logger, BuildConfig);
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
    /// Creates and configures an <see cref="HttpClient"/> for the specified named Mailpit connection.
    /// </summary>
    public HttpClient CreateClient(string? connectionName) {
        ConnectionConfig cfg = Get(connectionName);
        HttpClient http = httpClientFactory.CreateClient(ClientKey);
        http.BaseAddress = new Uri(cfg.Url.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return http;
    }

    /// <inheritdoc />
    public void Dispose() => core.Dispose();

    private static ConnectionConfig BuildConfig(ConnectionValues values) =>
        new(values.Name, values.GetString("URL"));
}
