namespace CloudyWing.McpLab.Redis;

/// <summary>
/// Stores host, port, password, and database index for a single named Redis connection.
/// </summary>
public sealed record ConnectionConfig(
    string Name, string Host, int Port, string Password, int Database
);
