namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Provides read-only MCP tools for inspecting OIDC providers and JWT tokens.
/// </summary>
[McpServerToolType]
public sealed class OidcTools {
    private const int DefaultClaimLimit = 50;
    private const int MaxClaimLimit = 200;
    private const int DefaultClockSkewSeconds = 60;
    private const int MaxClockSkewSeconds = 600;
    private readonly ConnectionRegistry registry;
    private readonly OidcDocumentClient documentClient;

    /// <summary>
    /// Initializes a new instance of <see cref="OidcTools"/>.
    /// </summary>
    public OidcTools(ConnectionRegistry registry, OidcDocumentClient documentClient) {
        this.registry = registry;
        this.documentClient = documentClient;
    }

    /// <summary>
    /// Lists all configured OIDC provider profiles.
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 OIDC provider profile，不回傳 token 或密鑰")]
    public string ListConnections() =>
        ToolResponse.Ok(registry.All.Select(kv => ToConnectionSummary(kv.Value)));

    /// <summary>
    /// Gets the OpenID Connect discovery document summary.
    /// </summary>
    [McpServerTool, Description("讀取 OIDC discovery document 摘要")]
    public async Task<string> GetDiscoveryDocument(
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            ConnectionConfig cfg = registry.Get(connection);
            OpenIdConnectConfiguration document = await documentClient.GetDiscoveryDocumentAsync(cfg)
                .ConfigureAwait(false);

            return ToolResponse.Ok(new {
                connection = ToConnectionSummary(cfg),
                issuer = document.Issuer ?? "",
                authorization_endpoint = document.AuthorizationEndpoint ?? "",
                token_endpoint = document.TokenEndpoint ?? "",
                userinfo_endpoint = document.UserInfoEndpoint ?? "",
                jwks_uri = document.JwksUri ?? "",
                end_session_endpoint = document.EndSessionEndpoint ?? "",
                response_types_supported = document.ResponseTypesSupported,
                grant_types_supported = document.GrantTypesSupported,
                scopes_supported = document.ScopesSupported,
                claims_supported = document.ClaimsSupported,
                id_token_signing_alg_values_supported = document.IdTokenSigningAlgValuesSupported,
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Gets public JSON Web Key Set summary.
    /// </summary>
    [McpServerTool, Description("讀取 OIDC JWKS 公鑰摘要")]
    public async Task<string> GetJwks(
        [Description("連線名稱，省略則使用第一個")] string connection = "",
        [Description("是否回傳 RSA/EC 公鑰參數，預設 false")] bool includePublicKeyParameters = false
    ) {
        try {
            ConnectionConfig cfg = registry.Get(connection);
            OpenIdConnectConfiguration discovery = await documentClient.GetDiscoveryDocumentAsync(cfg)
                .ConfigureAwait(false);
            IReadOnlyList<JsonWebKey> keys = await documentClient.GetJsonWebKeysAsync(cfg, discovery)
                .ConfigureAwait(false);

            return ToolResponse.Ok(new {
                connection = ToConnectionSummary(cfg),
                jwks_uri = discovery.JwksUri ?? "",
                keys = keys.Select(key => ToJwkSummary(key, includePublicKeyParameters)),
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Reads JWT header and claims without validating the signature.
    /// </summary>
    [McpServerTool, Description("解析 JWT header 與 claims；不驗證簽章")]
    public string InspectJwt(
        [Description("JWT 或 Bearer token")] string token,
        [Description("是否回傳 claims 清單，預設 false")] bool includeClaims = false,
        [Description("claims 最大回傳數，預設 50，上限 200")] int claimLimit = 0
    ) {
        try {
            int safeClaimLimit = NormalizeClaimLimit(claimLimit);
            JwtTokenSummary summary = JwtTokenProjector.Inspect(token, includeClaims, safeClaimLimit);

            return ToolResponse.Ok(new {
                signature_validated = false,
                claim_limit = safeClaimLimit,
                jwt = summary,
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Validates a JWT with the configured OIDC provider JWKS.
    /// </summary>
    [McpServerTool, Description("使用指定 OIDC provider 的 JWKS 驗證 JWT 簽章與 issuer/audience/lifetime")]
    public async Task<string> ValidateJwt(
        [Description("JWT 或 Bearer token")] string token,
        [Description("連線名稱，省略則使用第一個")] string connection = "",
        [Description("覆寫 audience；省略時使用 profile 設定，若兩者都空則不驗證 audience")] string audience = "",
        [Description("是否驗證 exp/nbf，預設 true")] bool validateLifetime = true,
        [Description("時間誤差秒數，預設 60，上限 600")] int clockSkewSeconds = 0
    ) {
        try {
            string normalizedToken = JwtTokenInput.Normalize(token);
            ConnectionConfig cfg = registry.Get(connection);
            OpenIdConnectConfiguration discovery = await documentClient.GetDiscoveryDocumentAsync(cfg)
                .ConfigureAwait(false);
            IReadOnlyList<JsonWebKey> keys = await documentClient.GetJsonWebKeysAsync(cfg, discovery)
                .ConfigureAwait(false);
            string targetAudience = string.IsNullOrWhiteSpace(audience) ? cfg.Audience : audience.Trim();
            int safeClockSkewSeconds = ToolRuntimeOptions.NormalizeRequestedInt32(
                clockSkewSeconds,
                DefaultClockSkewSeconds,
                0,
                MaxClockSkewSeconds
            );

            return ValidateToken(
                normalizedToken,
                cfg,
                discovery,
                keys,
                targetAudience,
                validateLifetime,
                safeClockSkewSeconds
            );
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    private static string ValidateToken(
        string token,
        ConnectionConfig cfg,
        OpenIdConnectConfiguration discovery,
        IReadOnlyList<JsonWebKey> keys,
        string audience,
        bool validateLifetime,
        int clockSkewSeconds
    ) {
        JwtSecurityTokenHandler handler = new();
        TokenValidationParameters parameters = new() {
            ValidateIssuer = true,
            ValidIssuers = GetValidIssuers(cfg, discovery),
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.FromSeconds(clockSkewSeconds),
        };

        try {
            ClaimsPrincipal principal = handler.ValidateToken(token, parameters, out SecurityToken validatedToken);
            JwtSecurityToken jwt = validatedToken as JwtSecurityToken
                ?? throw new SecurityTokenException("Validated token is not a JWT.");

            return ToolResponse.Ok(new {
                valid = true,
                connection = ToConnectionSummary(cfg),
                audience_validated = parameters.ValidateAudience,
                lifetime_validated = validateLifetime,
                clock_skew_seconds = clockSkewSeconds,
                subject = principal.FindFirstValue("sub") ?? "",
                jwt = JwtTokenProjector.CreateSummary(jwt, includeClaims: false, claimLimit: 0),
            });
        } catch (SecurityTokenException ex) {
            return ToolResponse.Ok(new {
                valid = false,
                reason = ex.Message,
                connection = ToConnectionSummary(cfg),
                audience_validated = parameters.ValidateAudience,
                lifetime_validated = validateLifetime,
                clock_skew_seconds = clockSkewSeconds,
            });
        } catch (ArgumentException ex) {
            return ToolResponse.Ok(new {
                valid = false,
                reason = ex.Message,
                connection = ToConnectionSummary(cfg),
                audience_validated = parameters.ValidateAudience,
                lifetime_validated = validateLifetime,
                clock_skew_seconds = clockSkewSeconds,
            });
        }
    }

    private static string[] GetValidIssuers(ConnectionConfig cfg, OpenIdConnectConfiguration discovery) {
        return new[] {
                cfg.Issuer,
                discovery.Issuer ?? "",
            }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static object ToConnectionSummary(ConnectionConfig cfg) =>
        new {
            name = cfg.Name,
            issuer = cfg.Issuer,
            discovery_url = cfg.DiscoveryUrl,
            audience_configured = !string.IsNullOrWhiteSpace(cfg.Audience),
            require_https_metadata = cfg.RequireHttpsMetadata,
        };

    private static object ToJwkSummary(JsonWebKey key, bool includePublicKeyParameters) =>
        new {
            kid = key.Kid ?? "",
            kty = key.Kty ?? "",
            use = key.Use ?? "",
            alg = key.Alg ?? "",
            key_ops = key.KeyOps,
            crv = key.Crv ?? "",
            x5t = key.X5t ?? "",
            x5c_count = key.X5c?.Count ?? 0,
            n = includePublicKeyParameters ? key.N ?? "" : "",
            e = includePublicKeyParameters ? key.E ?? "" : "",
            x = includePublicKeyParameters ? key.X ?? "" : "",
            y = includePublicKeyParameters ? key.Y ?? "" : "",
        };

    private static int NormalizeClaimLimit(int claimLimit) =>
        ToolRuntimeOptions.NormalizeRequestedInt32(claimLimit, DefaultClaimLimit, 1, MaxClaimLimit);
}
