namespace CloudyWing.McpLab.Elastic;

/// <summary>
/// Stores URL, credentials, and SSL settings for a single named Elasticsearch connection.
/// </summary>
public sealed record ConnectionConfig(
    string Name, string Url, string User, string Password, bool SslSkipVerify
);
