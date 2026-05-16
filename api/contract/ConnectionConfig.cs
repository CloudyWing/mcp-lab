namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Represents a named API contract profile.
/// </summary>
public sealed record ConnectionConfig(
    string Name,
    string SpecUrl,
    string SpecPath,
    string BaseUrl,
    IReadOnlySet<string> AllowedMethods,
    bool InvokeEnabled,
    bool SslSkipVerify,
    string AuthHeaderName,
    string AuthHeaderValue
);
