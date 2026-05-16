namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Represents a projected JWT claim value.
/// </summary>
public sealed record JwtClaimValue(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string Value
);
