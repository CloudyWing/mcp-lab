using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

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
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        IReadOnlyDictionary<string, ConnectionConfig> all = All;

        if (all.Count == 0) {
            throw new InvalidOperationException(
                "No API contract profiles configured. Set API_CONTRACT_CONN_<name>_SPEC_URL or SPEC_PATH."
            );
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

    /// <summary>
    /// Creates an <see cref="HttpClient"/> for the specified profile.
    /// </summary>
    public HttpClient CreateClient(ConnectionConfig cfg) =>
        httpClientFactory.CreateClient(cfg.SslSkipVerify ? SslSkipClientKey : PlainClientKey);

    private void HandleDotEnvReloaded(object? sender, DotEnvReloadedEventArgs e) {
        Rebuild(e.Current);
    }

    private void Rebuild(IReadOnlyDictionary<string, string> snapshot) {
        IReadOnlyDictionary<string, ConnectionConfig> previous = All;
        Dictionary<string, Dictionary<string, string>> buckets =
            EnvironmentConnectionSettings.LoadBuckets(snapshot, EnvRegex());

        Dictionary<string, ConnectionConfig> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            if (!EnvironmentConnectionSettings.HasConfiguredConnection(cfg, "name", "spec_url", "spec_path")) {
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
        string name = EnvironmentConnectionSettings.GetRequiredString(cfg, aliasKey, "API_CONTRACT_CONN", "name");
        string specUrl = EnvironmentConnectionSettings.GetString(cfg, "spec_url");
        string specPath = EnvironmentConnectionSettings.GetString(cfg, "spec_path");

        if (string.IsNullOrWhiteSpace(specUrl) && string.IsNullOrWhiteSpace(specPath)) {
            throw new InvalidOperationException(
                $"Connection '{aliasKey}': API_CONTRACT_CONN_{aliasKey}_SPEC_URL or SPEC_PATH is required."
            );
        }

        return new ConnectionConfig(
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

    [LoggerMessage(
        EventId = 2801,
        Level = LogLevel.Warning,
        Message = "API contract connection '{AliasKey}' rebuild failed: {Reason}; previous configuration retained when available"
    )]
    private static partial void LogConnectionRebuildFailed(ILogger logger, string aliasKey, string reason);
}
