namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Represents a named OIDC provider profile.
/// </summary>
public sealed record ConnectionConfig(
    string Name,
    string Issuer,
    string DiscoveryUrl,
    string Audience,
    bool RequireHttpsMetadata
);
