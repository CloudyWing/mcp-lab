namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Loads and exposes named OIDC provider profiles from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    /// <summary>
    /// Named HTTP client key used for OIDC metadata and JWKS calls.
    /// </summary>
    public const string ClientKey = "oidc";

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/> by loading environment variables.
    /// </summary>
    public ConnectionRegistry() => All = Load();

    /// <summary>
    /// Gets all loaded connections, keyed by connection name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, ConnectionConfig> All { get; }

    /// <summary>
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        if (All.Count == 0) {
            throw new InvalidOperationException("No OIDC connections configured. Set OIDC_CONN_<name>_ISSUER.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return All.Values.First();
        }

        return All.TryGetValue(name, out ConnectionConfig? cfg)
            ? cfg
            : throw new KeyNotFoundException(
                $"Connection '{name}' not found. Available: {string.Join(", ", All.Keys)}"
            );
    }

    private static Dictionary<string, ConnectionConfig> Load() {
        Dictionary<string, Dictionary<string, string>> buckets =
            EnvironmentConnectionSettings.LoadBuckets(EnvRegex());

        Dictionary<string, ConnectionConfig> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            if (!EnvironmentConnectionSettings.HasConfiguredConnection(
                cfg,
                "name",
                "issuer",
                "discovery_url",
                "metadata_url"
            )) {
                continue;
            }

            string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "OIDC_CONN", "name");
            string issuer = NormalizeIssuer(
                EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "OIDC_CONN", "issuer")
            );
            string discoveryUrl = GetDiscoveryUrl(cfg, issuer);
            string audience = EnvironmentConnectionSettings.GetString(cfg, "audience");
            bool requireHttpsMetadata = GetRequireHttpsMetadata(cfg, aliasKey);

            result[name] = new ConnectionConfig(name, issuer, discoveryUrl, audience, requireHttpsMetadata);
        }

        return result;
    }

    private static string NormalizeIssuer(string issuer) => issuer.Trim().TrimEnd('/');

    private static string GetDiscoveryUrl(IReadOnlyDictionary<string, string> cfg, string issuer) {
        string configured = EnvironmentConnectionSettings.GetString(cfg, "discovery_url");

        if (string.IsNullOrWhiteSpace(configured)) {
            configured = EnvironmentConnectionSettings.GetString(cfg, "metadata_url");
        }

        return string.IsNullOrWhiteSpace(configured)
            ? $"{issuer}/.well-known/openid-configuration"
            : configured.Trim();
    }

    private static bool GetRequireHttpsMetadata(IReadOnlyDictionary<string, string> cfg, string aliasKey) {
        string value = EnvironmentConnectionSettings.GetString(cfg, "require_https_metadata");

        if (string.IsNullOrWhiteSpace(value)) {
            return true;
        }

        if (!bool.TryParse(value, out bool result)) {
            throw new InvalidOperationException(
                $"Connection '{aliasKey}': OIDC_CONN_{aliasKey}_REQUIRE_HTTPS_METADATA must be true or false."
            );
        }

        return result;
    }

    [GeneratedRegex(@"^OIDC_CONN_([A-Z0-9]+)_([A-Z0-9_]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();
}
