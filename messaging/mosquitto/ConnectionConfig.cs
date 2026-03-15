namespace CloudyWing.McpLab.Mosquitto;

/// <summary>
/// Stores host, port, and credentials for a single named MQTT broker connection.
/// </summary>
public sealed record ConnectionConfig(
    string Name, string Host, int Port, string User, string Password
);
