using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Mosquitto;

/// <summary>
/// Loads and exposes named MQTT broker connection configurations from environment variables.
/// </summary>
public sealed class ConnectionRegistry : IConnectionStatusSource, IDisposable {
    private readonly ConnectionRegistryCore<ConnectionConfig> core;

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/>.
    /// </summary>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    /// <param name="logger">The logger.</param>
    public ConnectionRegistry(IDotEnvSnapshotProvider snapshotProvider, ILogger<ConnectionRegistry> logger) {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(logger);

        core = new(ConnectionSchemas.Mqtt, snapshotProvider, logger, BuildConfig);
    }

    /// <summary>
    /// Gets all loaded connections, keyed by connection name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, ConnectionConfig> All => core.All;

    /// <inheritdoc />
    public ConnectionRegistryStatus Status => core.Status;

    /// <summary>
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty.
    /// </summary>
    public ConnectionConfig Get(string? name) => core.Get(name);

    /// <summary>
    /// Gets the ENVIRONMENT label of a connection, or an empty string.
    /// </summary>
    public string EnvironmentOf(string name) => core.EnvironmentOf(name);

    /// <summary>
    /// Establishes an MQTT connection and returns a connected <see cref="IMqttClient"/> for the specified named connection.
    /// </summary>
    public async Task<IMqttClient> ConnectAsync(string? connectionName) {
        ConnectionConfig cfg = Get(connectionName);
        MqttFactory factory = new();
        IMqttClient client = factory.CreateMqttClient();
        MqttClientOptionsBuilder optBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(cfg.Host, cfg.Port)
            .WithTimeout(TimeSpan.FromSeconds(10))
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(15));

        if (!string.IsNullOrEmpty(cfg.User)) {
            optBuilder = optBuilder.WithCredentials(cfg.User, cfg.Password);
        }
        await client.ConnectAsync(optBuilder.Build(), CancellationToken.None);
        return client;
    }

    /// <inheritdoc />
    public void Dispose() => core.Dispose();

    private static ConnectionConfig BuildConfig(ConnectionValues values) =>
        new(
            Name: values.Name,
            Host: values.GetString("HOST"),
            Port: values.GetInt("PORT"),
            User: values.GetString("USER"),
            Password: values.GetString("PASSWORD")
        );
}
