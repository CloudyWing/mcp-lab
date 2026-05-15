namespace CloudyWing.McpLab.Mailpit;

/// <summary>
/// Stores the base URL for a single named Mailpit connection.
/// </summary>
public sealed record ConnectionConfig(
    string Name, string Url
);
