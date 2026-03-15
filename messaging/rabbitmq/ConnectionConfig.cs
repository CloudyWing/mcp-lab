namespace CloudyWing.McpLab.RabbitMq;

/// <summary>
/// Stores host, management port, and credentials for a single named RabbitMQ connection.
/// </summary>
public sealed record ConnectionConfig(
    string Name, string Host, int MgmtPort, string User, string Password
);
