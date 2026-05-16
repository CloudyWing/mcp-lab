namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Loads and exposes named API contract profiles from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    /// <summary>
    /// Named HTTP client key for normal TLS validation.
    /// </summary>
    public const string PlainClientKey = "api-contract-plain";

    /// <summary>
    /// Named HTTP client key for intentionally skipping TLS validation.
    /// </summary>
    public const string SslSkipClientKey = "api-contract-ssl-skip";

    private static readonly string[] DefaultAllowedMethods = ["GET", "HEAD", "OPTIONS"];
    private readonly IHttpClientFactory httpClientFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/> by loading environment variables.
    /// </summary>
    public ConnectionRegistry(IHttpClientFactory httpClientFactory) {
        this.httpClientFactory = httpClientFactory;
        All = Load();
    }

    /// <summary>
    /// Gets all loaded connections, keyed by connection name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, ConnectionConfig> All { get; }

    /// <summary>
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        if (All.Count == 0) {
            throw new InvalidOperationException(
                "No API contract profiles configured. Set API_CONTRACT_CONN_<name>_SPEC_URL or SPEC_PATH."
            );
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

    /// <summary>
    /// Creates an <see cref="HttpClient"/> for the specified profile.
    /// </summary>
    public HttpClient CreateClient(ConnectionConfig cfg) =>
        httpClientFactory.CreateClient(cfg.SslSkipVerify ? SslSkipClientKey : PlainClientKey);

    private static Dictionary<string, ConnectionConfig> Load() {
        Dictionary<string, Dictionary<string, string>> buckets =
            EnvironmentConnectionSettings.LoadBuckets(EnvRegex());

        Dictionary<string, ConnectionConfig> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            if (!EnvironmentConnectionSettings.HasConfiguredConnection(cfg, "name", "spec_url", "spec_path")) {
                continue;
            }

            string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "API_CONTRACT_CONN", "name");
            string specUrl = EnvironmentConnectionSettings.GetString(cfg, "spec_url");
            string specPath = EnvironmentConnectionSettings.GetString(cfg, "spec_path");

            if (string.IsNullOrWhiteSpace(specUrl) && string.IsNullOrWhiteSpace(specPath)) {
                throw new InvalidOperationException(
                    $"Connection '{aliasKey}': API_CONTRACT_CONN_{aliasKey}_SPEC_URL or SPEC_PATH is required."
                );
            }

            result[name] = new ConnectionConfig(
                name,
                specUrl,
                specPath,
                EnvironmentConnectionSettings.GetString(cfg, "base_url"),
                ParseAllowedMethods(EnvironmentConnectionSettings.GetString(cfg, "allowed_methods")),
                GetOptionalBool(cfg, aliasKey, "invoke_enabled", defaultValue: false),
                GetOptionalBool(cfg, aliasKey, "ssl_skip_verify", defaultValue: false),
                EnvironmentConnectionSettings.GetString(cfg, "auth_header_name"),
                EnvironmentConnectionSettings.GetString(cfg, "auth_header_value")
            );
        }

        return result;
    }

    private static IReadOnlySet<string> ParseAllowedMethods(string value) {
        string[] methods = string.IsNullOrWhiteSpace(value)
            ? DefaultAllowedMethods
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return methods
            .Select(method => method.ToUpperInvariant())
            .Where(method => !string.IsNullOrWhiteSpace(method))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool GetOptionalBool(
        IReadOnlyDictionary<string, string> cfg,
        string aliasKey,
        string field,
        bool defaultValue
    ) {
        string value = EnvironmentConnectionSettings.GetString(cfg, field);

        if (string.IsNullOrWhiteSpace(value)) {
            return defaultValue;
        }

        if (!bool.TryParse(value, out bool result)) {
            throw new InvalidOperationException(
                $"Connection '{aliasKey}': API_CONTRACT_CONN_{aliasKey}_{field.ToUpperInvariant()} " +
                "must be true or false."
            );
        }

        return result;
    }

    [GeneratedRegex(@"^API_CONTRACT_CONN_([A-Z0-9]+)_([A-Z0-9_]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();
}
