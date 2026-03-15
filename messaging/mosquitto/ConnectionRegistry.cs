namespace CloudyWing.McpLab.Mosquitto;

/// <summary>
/// Loads and exposes named MQTT broker connection configurations from environment variables.
/// </summary>
public sealed partial class ConnectionRegistry {
    /// <summary>
    /// Gets all loaded connections, keyed by connection name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, ConnectionConfig> All { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionRegistry"/> by loading environment variables.
    /// </summary>
    public ConnectionRegistry() => All = Load();

    /// <summary>
    /// Returns the named connection, or the first available when <paramref name="name"/> is empty; throws <see cref="KeyNotFoundException"/> when not found.
    /// </summary>
    public ConnectionConfig Get(string? name) {
        if (All.Count == 0) {
            throw new InvalidOperationException("No MQTT connections configured. Set MQTT_CONN_<name>_HOST in environment.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
            return All.Values.First();
        }

        return All.TryGetValue(name, out ConnectionConfig? cfg)
            ? cfg
            : throw new KeyNotFoundException($"Connection '{name}' not found. Available: {string.Join(", ", All.Keys)}");
    }

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

    private static Dictionary<string, ConnectionConfig> Load() {
        Dictionary<string, Dictionary<string, string>> buckets =
            new(StringComparer.OrdinalIgnoreCase);

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

        Dictionary<string, ConnectionConfig> result =
            new(StringComparer.OrdinalIgnoreCase);

        foreach ((string aliasKey, Dictionary<string, string> cfg) in buckets) {
            string host = (cfg.GetValueOrDefault("host") ?? "").Trim();

            string name = (cfg.GetValueOrDefault("name") ?? "").Trim();

            if (string.IsNullOrEmpty(name)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': MQTT_CONN_{aliasKey}_NAME is required.");
            }

            if (string.IsNullOrEmpty(host)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': MQTT_CONN_{aliasKey}_HOST is required.");
            }

            string portStr = (cfg.GetValueOrDefault("port") ?? "").Trim();
            int port = 1883;

            if (!string.IsNullOrEmpty(portStr)) {
                if (!int.TryParse(portStr, out port) || port <= 0) {
                    throw new InvalidOperationException($"Connection '{aliasKey}': MQTT_CONN_{aliasKey}_PORT must be a valid port number.");
                }
            }

            string user = (cfg.GetValueOrDefault("user") ?? "").Trim();

            if (string.IsNullOrEmpty(user)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': MQTT_CONN_{aliasKey}_USER is required.");
            }

            string password = (cfg.GetValueOrDefault("password") ?? "").Trim();

            if (string.IsNullOrEmpty(password)) {
                throw new InvalidOperationException($"Connection '{aliasKey}': MQTT_CONN_{aliasKey}_PASSWORD is required.");
            }

            result[name] = new ConnectionConfig(
                Name: name,
                Host: host,
                Port: port,
                User: user,
                Password: password
            );
        }

        return result;
    }

    [GeneratedRegex(@"^MQTT_CONN_([A-Z0-9]+)_([A-Z0-9]+)$", RegexOptions.Compiled)]
    private static partial Regex EnvRegex();
}
