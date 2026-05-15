namespace CloudyWing.McpLab.Elastic;

/// <summary>
/// Loads and exposes named Elasticsearch connection configurations from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    /// <summary>
    /// Named HTTP client key used for connections with default TLS validation.
    /// </summary>
    public const string PlainClientKey = "elastic";

    /// <summary>
    /// Named HTTP client key used for connections that skip TLS certificate validation.
    /// </summary>
    public const string SslSkipClientKey = "elastic-ssl-skip";

    private readonly IHttpClientFactory httpClientFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/> with the specified HTTP client factory.
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
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty; throws <see cref="KeyNotFoundException"/> when not found.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        if (All.Count == 0) {
            throw new InvalidOperationException("No Elasticsearch connections configured. Set ES_CONN_<name>_URL in environment.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return All.Values.First();
        }

        return All.TryGetValue(name, out ConnectionConfig? cfg)
            ? cfg
            : throw new KeyNotFoundException($"Connection '{name}' not found. Available: {string.Join(", ", All.Keys)}");
    }

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

    private static Dictionary<string, ConnectionConfig> Load() {
        Dictionary<string, Dictionary<string, string>> buckets =
            EnvironmentConnectionSettings.LoadBuckets(EnvRegex());

        Dictionary<string, ConnectionConfig> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            if (!EnvironmentConnectionSettings.HasConfiguredConnection(
                cfg,
                "name",
                "url",
                "user",
                "password",
                "ssl_skip_verify"
            )) {
                continue;
            }

            string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "ES_CONN", "name");
            string url = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "ES_CONN", "url");
            string user = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "ES_CONN", "user");
            string password = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "ES_CONN", "password");
            bool sslSkipVerify = EnvironmentConnectionSettings.GetRequiredBool(
                cfg,
                aliasKey,
                "ES_CONN",
                "ssl_skip_verify"
            );

            result[name] = new ConnectionConfig(
                Name: name,
                Url: url,
                User: user,
                Password: password,
                SslSkipVerify: sslSkipVerify
            );
        }

        return result;
    }

    [GeneratedRegex(@"^ES_CONN_([A-Z0-9]+)_([A-Z0-9_]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();
}
