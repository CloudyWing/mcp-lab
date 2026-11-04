using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Loads and exposes named API contract profiles from environment variables.
/// </summary>
public sealed class ConnectionRegistry : IConnectionStatusSource, IDisposable {
    /// <summary>
    /// Named HTTP client key for normal TLS validation.
    /// </summary>
    public const string PlainClientKey = "api-contract-plain";

    /// <summary>
    /// Named HTTP client key for intentionally skipping TLS validation.
    /// </summary>
    public const string SslSkipClientKey = "api-contract-ssl-skip";

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
        core = new(ConnectionSchemas.ApiContract, snapshotProvider, logger, BuildConfig);
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
    /// Creates an <see cref="HttpClient"/> for the specified profile.
    /// </summary>
    public HttpClient CreateClient(ConnectionConfig cfg) {
        HttpClient http = httpClientFactory.CreateClient(cfg.SslSkipVerify ? SslSkipClientKey : PlainClientKey);
        http.Timeout = ServiceSettings.ApiContract.HttpTimeout.ReadSeconds(snapshotProvider.Current);

        return http;
    }

    /// <inheritdoc />
    public void Dispose() => core.Dispose();

    private static ConnectionConfig BuildConfig(ConnectionValues values) =>
        new(
            values.Name,
            values.GetString("SPEC_URL"),
            values.GetString("SPEC_PATH"),
            values.GetString("BASE_URL"),
            values.GetList("ALLOWED_METHODS")
                .Select(method => method.ToUpperInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            values.GetBool("INVOKE_ENABLED"),
            values.GetBool("SSL_SKIP_VERIFY"),
            values.GetString("AUTH_HEADER_NAME"),
            values.GetString("AUTH_HEADER_VALUE")
        );
}
