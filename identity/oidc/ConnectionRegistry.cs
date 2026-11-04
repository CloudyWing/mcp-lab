using CloudyWing.McpLab.Shared.Configuration;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Loads and exposes named OIDC provider profiles from environment variables.
/// </summary>
public sealed class ConnectionRegistry : IConnectionStatusSource, IDisposable {
    /// <summary>
    /// Named HTTP client key used for OIDC metadata and JWKS calls.
    /// </summary>
    public const string ClientKey = "oidc";

    private readonly ConnectionRegistryCore<ConnectionConfig> core;

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/>.
    /// </summary>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    /// <param name="logger">The logger.</param>
    public ConnectionRegistry(IDotEnvSnapshotProvider snapshotProvider, ILogger<ConnectionRegistry> logger) {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(logger);

        core = new(ConnectionSchemas.Oidc, snapshotProvider, logger, BuildConfig);
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

    /// <inheritdoc />
    public void Dispose() => core.Dispose();

    private static ConnectionConfig BuildConfig(ConnectionValues values) {
        string issuer = values.GetString("ISSUER").TrimEnd('/');
        string discoveryUrl = values.GetString("DISCOVERY_URL");

        return new ConnectionConfig(
            values.Name,
            issuer,
            string.IsNullOrEmpty(discoveryUrl) ? $"{issuer}/.well-known/openid-configuration" : discoveryUrl,
            values.GetString("AUDIENCE"),
            values.GetBool("REQUIRE_HTTPS_METADATA")
        );
    }
}
