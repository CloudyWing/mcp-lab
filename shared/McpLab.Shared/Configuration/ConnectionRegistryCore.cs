using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Shared.Configuration;

/// <summary>
/// Load result of one connection, as reported by <c>/config/status</c>.
/// </summary>
/// <param name="Alias">Alias part of the variable names.</param>
/// <param name="Name">Connection name used by tools.</param>
/// <param name="State"><c>loaded</c>, <c>rejected</c>, or <c>rejected_using_previous</c>.</param>
/// <param name="Errors">Reasons the latest settings were rejected.</param>
public sealed record ConnectionStatus(string Alias, string Name, string State, IReadOnlyList<string> Errors);

/// <summary>
/// Load result of all connections of one service after the latest reload.
/// </summary>
public sealed record ConnectionRegistryStatus(
    ConnectionSchema Schema,
    DateTimeOffset LoadedAt,
    IReadOnlyList<ConnectionStatus> Connections,
    IReadOnlyList<IgnoredSetting> Ignored
);

/// <summary>
/// Exposes the latest connection load result of a service.
/// </summary>
public interface IConnectionStatusSource {
    /// <summary>Gets the latest load result.</summary>
    ConnectionRegistryStatus Status { get; }
}

/// <summary>
/// Shared logic behind every service's connection registry: parses settings from the .env snapshot,
/// rebuilds connections on reload, keeps the previous configuration of a connection whose new settings
/// are invalid, and records the load result.
/// </summary>
public sealed partial class ConnectionRegistryCore<TConfig> : IConnectionStatusSource, IDisposable
    where TConfig : class {
    private readonly ConnectionSchema schema;
    private readonly IDotEnvSnapshotProvider snapshotProvider;
    private readonly ILogger logger;
    private readonly Func<ConnectionValues, TConfig> build;
    private IReadOnlyDictionary<string, TConfig> current =
        new Dictionary<string, TConfig>(StringComparer.OrdinalIgnoreCase);
    private ConnectionRegistryStatus status;
    private IReadOnlyDictionary<string, string> environments =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance and loads the current snapshot.
    /// </summary>
    public ConnectionRegistryCore(
        ConnectionSchema schema,
        IDotEnvSnapshotProvider snapshotProvider,
        ILogger logger,
        Func<ConnectionValues, TConfig> build
    ) {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(build);

        this.schema = schema;
        this.snapshotProvider = snapshotProvider;
        this.logger = logger;
        this.build = build;
        status = new ConnectionRegistryStatus(schema, DateTimeOffset.UtcNow, [], []);
        Rebuild(snapshotProvider.Current);
        snapshotProvider.Reloaded += HandleReloaded;
    }

    /// <summary>
    /// Raised after connections are rebuilt, with the new set of connections.
    /// </summary>
    public event Action<IReadOnlyDictionary<string, TConfig>>? Rebuilt;

    /// <summary>
    /// Gets all loaded connections, keyed by connection name (case-insensitive), in configuration order.
    /// </summary>
    public IReadOnlyDictionary<string, TConfig> All => Volatile.Read(ref current);

    /// <inheritdoc />
    public ConnectionRegistryStatus Status => Volatile.Read(ref status);

    /// <summary>
    /// Gets the ENVIRONMENT label of a loaded connection, or an empty string when it has none.
    /// </summary>
    public string EnvironmentOf(string name) => Volatile.Read(ref environments).GetValueOrDefault(name) ?? "";

    /// <summary>
    /// Returns the named connection, or the first one when <paramref name="name"/> is empty.
    /// </summary>
    public TConfig Get(string? name) {
        IReadOnlyDictionary<string, TConfig> all = All;

        if (all.Count == 0) {
            throw new InvalidOperationException(
                $"No connections configured for {schema.Service}. Set {schema.Prefix}_<ALIAS>_* in config/{schema.Service}/service.env.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return all.Values.First();
        }

        return all.TryGetValue(name, out TConfig? config)
            ? config
            : throw new KeyNotFoundException($"Connection '{name}' not found. Available: {string.Join(", ", all.Keys)}");
    }

    /// <inheritdoc />
    public void Dispose() {
        snapshotProvider.Reloaded -= HandleReloaded;
    }

    private void HandleReloaded(object? sender, DotEnvReloadedEventArgs e) {
        Rebuild(e.Current);
    }

    private void Rebuild(IReadOnlyDictionary<string, string> snapshot) {
        IReadOnlyDictionary<string, TConfig> previous = All;
        IReadOnlyDictionary<string, string> previousEnvironments = Volatile.Read(ref environments);
        Dictionary<string, string> nextEnvironments = new(StringComparer.OrdinalIgnoreCase);
        ConnectionParseResult parsed = ConnectionSettingsParser.Parse(schema, snapshot);
        Dictionary<string, TConfig> result = new(StringComparer.OrdinalIgnoreCase);
        List<ConnectionStatus> statuses = [];
        List<RejectedConnection> rejected = [.. parsed.Rejected];

        foreach (ConnectionValues values in parsed.Connections) {
            try {
                result[values.Name] = build(values);
                nextEnvironments[values.Name] = values.GetString(ConnectionSchema.EnvironmentField);
                statuses.Add(new ConnectionStatus(values.Alias, values.Name, "loaded", []));
            } catch (InvalidOperationException ex) {
                rejected.Add(new RejectedConnection(values.Alias, values.Name, [ex.Message]));
            }
        }

        foreach (RejectedConnection item in rejected) {
            bool retained = !result.ContainsKey(item.Name) && previous.TryGetValue(item.Name, out TConfig? previousConfig)
                && result.TryAdd(item.Name, previousConfig);
            if (retained && previousEnvironments.TryGetValue(item.Name, out string? previousEnvironment)) {
                nextEnvironments[item.Name] = previousEnvironment;
            }

            statuses.Add(new ConnectionStatus(
                item.Alias,
                item.Name,
                retained ? "rejected_using_previous" : "rejected",
                item.Errors
            ));
            LogConnectionRejected(logger, schema.Service, item.Alias, string.Join(" ", item.Errors), retained);
        }

        foreach (IgnoredSetting item in parsed.Ignored) {
            LogSettingIgnored(logger, schema.Service, item.Key, item.Reason);
        }

        Volatile.Write(ref current, result);
        Volatile.Write(ref environments, nextEnvironments);
        Volatile.Write(ref status, new ConnectionRegistryStatus(schema, DateTimeOffset.UtcNow, statuses, parsed.Ignored));
        Rebuilt?.Invoke(result);
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "{Service} connection '{Alias}' rejected: {Reason} Previous configuration retained: {Retained}"
    )]
    private static partial void LogConnectionRejected(ILogger logger, string service, string alias, string reason, bool retained);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "{Service} setting {Key} ignored: {Reason}"
    )]
    private static partial void LogSettingIgnored(ILogger logger, string service, string key, string reason);
}
