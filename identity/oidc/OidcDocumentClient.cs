using System.Collections.Concurrent;
using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Reads OpenID Connect discovery documents and JWKS documents.
/// </summary>
public sealed class OidcDocumentClient {
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IDotEnvSnapshotProvider? snapshotProvider;
    private readonly ConcurrentDictionary<string, (string Body, DateTimeOffset ExpiresAt)> cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastForcedRefresh = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Minimum time between forced refreshes of one URL, so tokens with unknown key ids cannot bypass the cache.
    /// </summary>
    public static readonly TimeSpan MinForcedRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of <see cref="OidcDocumentClient"/>.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="snapshotProvider">The .env snapshot provider used to read OIDC_HTTP_TIMEOUT.</param>
    /// <param name="timeProvider">Clock used for cache expiry; defaults to the system clock.</param>
    public OidcDocumentClient(
        IHttpClientFactory httpClientFactory,
        IDotEnvSnapshotProvider? snapshotProvider = null,
        TimeProvider? timeProvider = null
    ) {
        this.httpClientFactory = httpClientFactory;
        this.snapshotProvider = snapshotProvider;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    private IReadOnlyDictionary<string, string> Settings => snapshotProvider?.Current ?? new Dictionary<string, string>();

    /// <summary>
    /// Gets the OpenID Connect discovery document for a profile. Responses are cached for
    /// OIDC_METADATA_CACHE_SECONDS unless <paramref name="forceRefresh"/> is set.
    /// </summary>
    public async Task<OpenIdConnectConfiguration> GetDiscoveryDocumentAsync(ConnectionConfig cfg, bool forceRefresh = false) {
        EnsureAllowedMetadataUrl(cfg.DiscoveryUrl, cfg.RequireHttpsMetadata);
        string json = await GetStringAsync(cfg.DiscoveryUrl, forceRefresh).ConfigureAwait(false);

        return OpenIdConnectConfiguration.Create(json);
    }

    /// <summary>
    /// Gets JSON Web Keys from the configured provider.
    /// </summary>
    public async Task<IReadOnlyList<JsonWebKey>> GetJsonWebKeysAsync(
        ConnectionConfig cfg,
        OpenIdConnectConfiguration? discovery = null,
        bool forceRefresh = false
    ) {
        OpenIdConnectConfiguration document = discovery ?? await GetDiscoveryDocumentAsync(cfg, forceRefresh).ConfigureAwait(false);
        string jwksUri = document.JwksUri ?? "";

        if (string.IsNullOrWhiteSpace(jwksUri)) {
            throw new InvalidOperationException("OIDC discovery document does not contain jwks_uri.");
        }

        EnsureAllowedMetadataUrl(jwksUri, cfg.RequireHttpsMetadata);
        string json = await GetStringAsync(jwksUri, forceRefresh).ConfigureAwait(false);
        JsonWebKeySet keySet = new(json);

        return keySet.Keys.ToArray();
    }

    private async Task<string> GetStringAsync(string url, bool forceRefresh) {
        int cacheSeconds = ServiceSettings.Oidc.MetadataCacheSeconds.Read(Settings);
        DateTimeOffset now = timeProvider.GetUtcNow();

        bool hasCached = cache.TryGetValue(url, out (string Body, DateTimeOffset ExpiresAt) cached) && cached.ExpiresAt > now;

        if (forceRefresh && hasCached && lastForcedRefresh.TryGetValue(url, out DateTimeOffset lastForced)
            && now - lastForced < MinForcedRefreshInterval) {
            forceRefresh = false;
        }

        if (!forceRefresh && cacheSeconds > 0 && hasCached) {
            return cached.Body;
        }

        if (forceRefresh) {
            lastForcedRefresh[url] = now;
        }

        string body = await FetchAsync(url).ConfigureAwait(false);

        if (cacheSeconds > 0) {
            cache[url] = (body, now.AddSeconds(cacheSeconds));
        } else {
            cache.TryRemove(url, out _);
        }

        return body;
    }

    private async Task<string> FetchAsync(string url) {
        using HttpClient http = httpClientFactory.CreateClient(ConnectionRegistry.ClientKey);
        http.Timeout = ServiceSettings.Oidc.HttpTimeout.ReadSeconds(Settings);
        using HttpResponseMessage response = await http.GetAsync(url).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"OIDC metadata endpoint returned HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}"
            );
        }

        return body;
    }

    private static void EnsureAllowedMetadataUrl(string url, bool requireHttpsMetadata) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) {
            throw new InvalidOperationException($"OIDC metadata URL is invalid: {url}");
        }

        if (requireHttpsMetadata && uri.Scheme != Uri.UriSchemeHttps) {
            throw new InvalidOperationException(
                "OIDC metadata URL must use HTTPS. Set OIDC_CONN_<name>_REQUIRE_HTTPS_METADATA=false for local dev."
            );
        }
    }
}
