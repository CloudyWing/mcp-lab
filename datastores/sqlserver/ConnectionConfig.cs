namespace CloudyWing.McpLab.SqlServer;

/// <summary>
/// Stores host, port, and credentials for a single named SQL Server connection.
/// </summary>
public sealed record ConnectionConfig(
    string Name, string Host, int Port, string User, string Password, string Database
);
