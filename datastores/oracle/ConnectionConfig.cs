namespace CloudyWing.McpLab.Oracle;

/// <summary>
/// Stores host, port, service name, and credentials for a single named Oracle connection.
/// </summary>
public sealed record ConnectionConfig(
    string Name, string Host, int Port, string Service, string User, string Password
);
