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
        Dictionary<string, Dictionary<string, string>> buckets = new(StringComparer.OrdinalIgnoreCase);

        foreach (string key in Environment.GetEnvironmentVariables().Keys.OfType<string>()) {
            string val = Environment.GetEnvironmentVariable(key) ?? "";
            Match match = EnvRegex().Match(key);

            if (!match.Success) {
                continue;
            }

            string aliasKey = match.Groups[1].Value;
            string field = match.Groups[2].Value.ToLowerInvariant();

            if (!buckets.TryGetValue(aliasKey, out Dictionary<string, string>? value)) {
                value = [];
                buckets[aliasKey] = value;
            }

            value[field] = val;
        }

        Dictionary<string, ConnectionConfig> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            string url = (cfg.GetValueOrDefault("url") ?? "").Trim();

            string name = (cfg.GetValueOrDefault("name") ?? "").Trim();

            if (string.IsNullOrEmpty(name)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ES_CONN_{aliasKey}_NAME is required.");
            }

            if (string.IsNullOrEmpty(url)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ES_CONN_{aliasKey}_URL is required.");
            }

            string user = (cfg.GetValueOrDefault("user") ?? "").Trim();

            if (string.IsNullOrEmpty(user)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ES_CONN_{aliasKey}_USER is required.");
            }

            string password = (cfg.GetValueOrDefault("password") ?? "").Trim();

            if (string.IsNullOrEmpty(password)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ES_CONN_{aliasKey}_PASSWORD is required.");
            }

            string sslSkipStr = (cfg.GetValueOrDefault("ssl_skip_verify") ?? "").Trim();

            if (string.IsNullOrEmpty(sslSkipStr)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ES_CONN_{aliasKey}_SSL_SKIP_VERIFY is required.");
            }

            if (!bool.TryParse(sslSkipStr, out bool sslSkipVerify)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': ES_CONN_{aliasKey}_SSL_SKIP_VERIFY must be true or false.");
            }

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
