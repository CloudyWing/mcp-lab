using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

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
    private readonly IDotEnvSnapshotProvider snapshotProvider;
    private readonly ILogger<ConnectionRegistry> logger;
    private IReadOnlyDictionary<string, ConnectionConfig> current =
        new Dictionary<string, ConnectionConfig>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/>.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    /// <param name="logger">The logger.</param>
    public ConnectionRegistry(
        IHttpClientFactory httpClientFactory,
        IDotEnvSnapshotProvider snapshotProvider,
        ILogger<ConnectionRegistry> logger
    ) {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClientFactory = httpClientFactory;
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
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty; throws <see cref="KeyNotFoundException"/> when not found.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        IReadOnlyDictionary<string, ConnectionConfig> all = All;

        if (all.Count == 0) {
            throw new InvalidOperationException("No RabbitMQ connections configured. Set RABBITMQ_CONN_<name>_HOST in environment.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return all.Values.First();
        }

        return all.TryGetValue(name, out ConnectionConfig? cfg)
            ? cfg
            : throw new KeyNotFoundException($"Connection '{name}' not found. Available: {string.Join(", ", all.Keys)}");
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
                "host",
                "user",
                "password"
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

        return new ConnectionConfig(
            Name: name,
            Host: host,
            MgmtPort: mgmtPort,
            User: user,
            Password: password
        );
    }

    [GeneratedRegex(@"^RABBITMQ_CONN_([A-Z0-9]+)_([A-Z0-9_]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();

    [LoggerMessage(
        EventId = 2601,
        Level = LogLevel.Warning,
        Message = "RabbitMQ connection '{AliasKey}' rebuild failed: {Reason}; previous configuration retained when available"
    )]
    private static partial void LogConnectionRebuildFailed(ILogger logger, string aliasKey, string reason);
}
