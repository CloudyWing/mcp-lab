namespace CloudyWing.McpLab.RabbitMq;

/// <summary>
/// Loads and exposes named RabbitMQ connection configurations from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    /// <summary>
    /// Named HTTP client key used for RabbitMQ Management API requests.
    /// </summary>
    public const string ClientKey = "rabbitmq";

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
            throw new InvalidOperationException("No RabbitMQ connections configured. Set RABBITMQ_CONN_<name>_HOST in environment.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return All.Values.First();
        }

        return All.TryGetValue(name, out ConnectionConfig? cfg)
            ? cfg
            : throw new KeyNotFoundException($"Connection '{name}' not found. Available: {string.Join(", ", All.Keys)}");
    }

    /// <summary>
    /// Creates and configures an <see cref="HttpClient"/> for the specified named RabbitMQ Management API connection.
    /// </summary>
    public HttpClient CreateClient(string? connectionName) {
        ConnectionConfig cfg = Get(connectionName);
        HttpClient http = httpClientFactory.CreateClient(ClientKey);
        http.BaseAddress = new Uri($"http://{cfg.Host}:{cfg.MgmtPort}/");

        string creds = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{cfg.User}:{cfg.Password}"));

        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", creds);
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
            string host = (cfg.GetValueOrDefault("host") ?? "").Trim();

            // support both MGMTPORT and MGMT_PORT key forms
            string mgmtPortStr = (
                cfg.GetValueOrDefault("mgmtport")
                    ?? cfg.GetValueOrDefault("mgmt_port")
                    ?? ""
            ).Trim();

            string name = (cfg.GetValueOrDefault("name") ?? "").Trim();

            if (string.IsNullOrEmpty(name)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': RABBITMQ_CONN_{aliasKey}_NAME is required.");
            }

            if (string.IsNullOrEmpty(host)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': RABBITMQ_CONN_{aliasKey}_HOST is required.");
            }

            int mgmtPort = 15672;

            if (!string.IsNullOrEmpty(mgmtPortStr)) {
                if (!int.TryParse(mgmtPortStr, out mgmtPort) || mgmtPort <= 0) {
                    throw new InvalidOperationException($"Connection '{aliasKey}': RABBITMQ_CONN_{aliasKey}_MGMTPORT must be a valid port number.");
                }
            }

            string user = (cfg.GetValueOrDefault("user") ?? "").Trim();

            if (string.IsNullOrEmpty(user)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': RABBITMQ_CONN_{aliasKey}_USER is required.");
            }

            string password = (cfg.GetValueOrDefault("password") ?? "").Trim();

            if (string.IsNullOrEmpty(password)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': RABBITMQ_CONN_{aliasKey}_PASSWORD is required.");
            }

            result[name] = new ConnectionConfig(
                Name: name,
                Host: host,
                MgmtPort: mgmtPort,
                User: user,
                Password: password
            );
        }

        return result;
    }

    [GeneratedRegex(@"^RABBITMQ_CONN_([A-Z0-9]+)_([A-Z0-9_]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();
}
