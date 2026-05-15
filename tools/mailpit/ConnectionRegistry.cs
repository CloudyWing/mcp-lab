namespace CloudyWing.McpLab.Mailpit;

/// <summary>
/// Loads and exposes named Mailpit connection configurations from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    /// <summary>
    /// Named HTTP client key used for Mailpit API calls.
    /// </summary>
    public const string ClientKey = "mailpit";

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
            throw new InvalidOperationException("No Mailpit connections configured. Set MAILPIT_CONN_<name>_URL.");
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
    /// Creates and configures an <see cref="HttpClient"/> for the specified named Mailpit connection.
    /// </summary>
    public HttpClient CreateClient(string? connectionName) {
        ConnectionConfig cfg = Get(connectionName);
        HttpClient http = httpClientFactory.CreateClient(ClientKey);
        http.BaseAddress = new Uri(cfg.Url.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return http;
    }

    private static Dictionary<string, ConnectionConfig> Load() {
        Dictionary<string, Dictionary<string, string>> buckets =
            EnvironmentConnectionSettings.LoadBuckets(EnvRegex());

        Dictionary<string, ConnectionConfig> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            if (!EnvironmentConnectionSettings.HasConfiguredConnection(cfg, "name", "url")) {
                continue;
            }

            string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MAILPIT_CONN", "name");
            string url = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "MAILPIT_CONN", "url");

            result[name] = new ConnectionConfig(name, url);
        }

        return result;
    }

    [GeneratedRegex(@"^MAILPIT_CONN_([A-Z0-9]+)_([A-Z0-9_]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();
}
