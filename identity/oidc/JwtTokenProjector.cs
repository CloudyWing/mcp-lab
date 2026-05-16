namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Projects JWT tokens into bounded summaries for MCP responses.
/// </summary>
public static class JwtTokenProjector {
    private static readonly JwtSecurityTokenHandler Handler = new();

    /// <summary>
    /// Reads a JWT without validating its signature.
    /// </summary>
    public static JwtTokenSummary Inspect(string token, bool includeClaims, int claimLimit) {
        string normalized = JwtTokenInput.Normalize(token);

        if (!Handler.CanReadToken(normalized)) {
            throw new ArgumentException("Input is not a readable JWT.", nameof(token));
        }

        JwtSecurityToken jwt = Handler.ReadJwtToken(normalized);
        return CreateSummary(jwt, includeClaims, claimLimit);
    }

    /// <summary>
    /// Creates a bounded summary from a parsed JWT.
    /// </summary>
    public static JwtTokenSummary CreateSummary(JwtSecurityToken jwt, bool includeClaims, int claimLimit) {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset? issuedAt = ToDateTimeOffsetOrNull(jwt.Payload.IssuedAt);
        DateTimeOffset? notBefore = ToDateTimeOffsetOrNull(jwt.ValidFrom);
        DateTimeOffset? expires = ToDateTimeOffsetOrNull(jwt.ValidTo);
        IReadOnlyList<string> scopes = GetScopes(jwt.Claims);
        IReadOnlyList<JwtClaimValue> claims = includeClaims
            ? jwt.Claims
                .Take(Math.Max(claimLimit, 0))
                .Select(claim => new JwtClaimValue(claim.Type, claim.Value))
                .ToArray()
            : [];

        return new JwtTokenSummary(
            jwt.Header.Alg ?? "",
            jwt.Header.Kid ?? "",
            jwt.Header.Typ ?? "",
            jwt.Issuer ?? "",
            jwt.Claims.FirstOrDefault(claim => claim.Type == "sub")?.Value ?? "",
            jwt.Audiences.ToArray(),
            scopes,
            issuedAt,
            notBefore,
            expires,
            expires.HasValue && expires.Value <= now,
            notBefore.HasValue && notBefore.Value > now,
            claims
        );
    }

    private static IReadOnlyList<string> GetScopes(IEnumerable<Claim> claims) {
        string[] names = ["scope", "scp"];

        return claims
            .Where(claim => names.Contains(claim.Type, StringComparer.Ordinal))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static DateTimeOffset? ToDateTimeOffsetOrNull(DateTime value) {
        if (value == DateTime.MinValue) {
            return null;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
