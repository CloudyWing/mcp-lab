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
        Dictionary<string, Dictionary<string, string>> buckets =
            EnvironmentConnectionSettings.LoadBuckets(EnvRegex());

        Dictionary<string, ConnectionConfig> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            if (!EnvironmentConnectionSettings.HasConfiguredConnection(
                cfg,
                "name",
                "host",
                "user",
                "password"
            )) {
                continue;
            }

            string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "RABBITMQ_CONN", "name");
            string host = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "RABBITMQ_CONN", "host");
            int mgmtPort = EnvironmentConnectionSettings.GetOptionalPort(
                cfg,
                aliasKey,
                "RABBITMQ_CONN",
                15672,
                "mgmtport",
                "mgmtport",
                "mgmt_port"
            );
            string user = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "RABBITMQ_CONN", "user");
            string password = EnvironmentConnectionSettings.GetRequiredString(
                cfg,
                aliasKey,
                "RABBITMQ_CONN",
                "password"
            );

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
