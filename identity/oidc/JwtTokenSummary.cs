namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Represents non-sensitive JWT header and claim summary information.
/// </summary>
public sealed record JwtTokenSummary(
    [property: JsonPropertyName("algorithm")]
    string Algorithm,
    [property: JsonPropertyName("key_id")]
    string KeyId,
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("issuer")]
    string Issuer,
    [property: JsonPropertyName("subject")]
    string Subject,
    [property: JsonPropertyName("audiences")]
    IReadOnlyList<string> Audiences,
    [property: JsonPropertyName("scopes")]
    IReadOnlyList<string> Scopes,
    [property: JsonPropertyName("issued_at")]
    DateTimeOffset? IssuedAt,
    [property: JsonPropertyName("not_before")]
    DateTimeOffset? NotBefore,
    [property: JsonPropertyName("expires")]
    DateTimeOffset? Expires,
    [property: JsonPropertyName("is_expired")]
    bool IsExpired,
    [property: JsonPropertyName("is_not_yet_valid")]
    bool IsNotYetValid,
    [property: JsonPropertyName("claims")]
    IReadOnlyList<JwtClaimValue> Claims
);
