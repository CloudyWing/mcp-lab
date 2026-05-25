using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Loads and exposes named OIDC provider profiles from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    /// <summary>
    /// Named HTTP client key used for OIDC metadata and JWKS calls.
    /// </summary>
    public const string ClientKey = "oidc";

    private readonly IDotEnvSnapshotProvider snapshotProvider;
    private readonly ILogger<ConnectionRegistry> logger;
    private IReadOnlyDictionary<string, ConnectionConfig> current =
        new Dictionary<string, ConnectionConfig>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/>.
    /// </summary>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    /// <param name="logger">The logger.</param>
    public ConnectionRegistry(IDotEnvSnapshotProvider snapshotProvider, ILogger<ConnectionRegistry> logger) {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.snapshotProvider = snapshotProvider;
        this.logger = logger;
        Rebuild(snapshotProvider.Current);
        this.snapshotProvider.Reloaded += HandleDotEnvReloaded;
    }

    /// <summary>
    /// Gets all loaded connections, keyed by connection name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, ConnectionConfig> All => Volatile.Read(ref current);

    /// <summary>
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        IReadOnlyDictionary<string, ConnectionConfig> all = All;

        if (all.Count == 0) {
            throw new InvalidOperationException("No OIDC connections configured. Set OIDC_CONN_<name>_ISSUER.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return all.Values.First();
        }

        return all.TryGetValue(name, out ConnectionConfig? cfg)
            ? cfg
            : throw new KeyNotFoundException(
                $"Connection '{name}' not found. Available: {string.Join(", ", all.Keys)}"
            );
    }

    private void HandleDotEnvReloaded(object? sender, DotEnvReloadedEventArgs e) {
        Rebuild(e.Current);
    }

    private void Rebuild(IReadOnlyDictionary<string, string> snapshot) {
        IReadOnlyDictionary<string, ConnectionConfig> previous = All;
        Dictionary<string, Dictionary<string, string>> buckets =
            EnvironmentConnectionSettings.LoadBuckets(snapshot, EnvRegex());

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

            try {
                ConnectionConfig config = BuildConfig(aliasKey, cfg);
                result[config.Name] = config;
            } catch (InvalidOperationException ex) {
                string configuredName = EnvironmentConnectionSettings.GetString(cfg, "name");

                if (!string.IsNullOrWhiteSpace(configuredName) && previous.TryGetValue(configuredName, out ConnectionConfig? previousConfig)) {
                    result[configuredName] = previousConfig;
                }

                LogConnectionRebuildFailed(logger, aliasKey, ex.Message);
            }
        }

        Volatile.Write(ref current, result);
    }

    private static ConnectionConfig BuildConfig(string aliasKey, IReadOnlyDictionary<string, string> cfg) {
        string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "OIDC_CONN", "name");
        string issuer = NormalizeIssuer(
            EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "OIDC_CONN", "issuer")
        );
        string discoveryUrl = GetDiscoveryUrl(cfg, issuer);
        string audience = EnvironmentConnectionSettings.GetString(cfg, "audience");
        bool requireHttpsMetadata = GetRequireHttpsMetadata(cfg, aliasKey);

        return new ConnectionConfig(name, issuer, discoveryUrl, audience, requireHttpsMetadata);
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

    [LoggerMessage(
        EventId = 2901,
        Level = LogLevel.Warning,
        Message = "OIDC connection '{AliasKey}' rebuild failed: {Reason}; previous configuration retained when available"
    )]
    private static partial void LogConnectionRebuildFailed(ILogger logger, string aliasKey, string reason);
}
