namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Reads OpenID Connect discovery documents and JWKS documents.
/// </summary>
public sealed class OidcDocumentClient {
    private readonly IHttpClientFactory httpClientFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="OidcDocumentClient"/>.
    /// </summary>
    public OidcDocumentClient(IHttpClientFactory httpClientFactory) {
        this.httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Gets the OpenID Connect discovery document for a profile.
    /// </summary>
    public async Task<OpenIdConnectConfiguration> GetDiscoveryDocumentAsync(ConnectionConfig cfg) {
        EnsureAllowedMetadataUrl(cfg.DiscoveryUrl, cfg.RequireHttpsMetadata);
        string json = await GetStringAsync(cfg.DiscoveryUrl).ConfigureAwait(false);

        return OpenIdConnectConfiguration.Create(json);
    }

    /// <summary>
    /// Gets JSON Web Keys from the configured provider.
    /// </summary>
    public async Task<IReadOnlyList<JsonWebKey>> GetJsonWebKeysAsync(
        ConnectionConfig cfg,
        OpenIdConnectConfiguration? discovery = null
    ) {
        OpenIdConnectConfiguration document = discovery ?? await GetDiscoveryDocumentAsync(cfg).ConfigureAwait(false);
        string jwksUri = document.JwksUri ?? "";

        if (string.IsNullOrWhiteSpace(jwksUri)) {
            throw new InvalidOperationException("OIDC discovery document does not contain jwks_uri.");
        }

        EnsureAllowedMetadataUrl(jwksUri, cfg.RequireHttpsMetadata);
        string json = await GetStringAsync(jwksUri).ConfigureAwait(false);
        JsonWebKeySet keySet = new(json);

        return keySet.Keys.ToArray();
    }

    private async Task<string> GetStringAsync(string url) {
        using HttpClient http = httpClientFactory.CreateClient(ConnectionRegistry.ClientKey);
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
