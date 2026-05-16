namespace CloudyWing.McpLab.Oidc;

/// <summary>
/// Normalizes JWT token input accepted by MCP tools.
/// </summary>
public static class JwtTokenInput {
    /// <summary>
    /// Strips the optional bearer scheme and validates that the token is not empty.
    /// </summary>
    public static string Normalize(string token) {
        string value = (token ?? "").Trim();

        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
            value = value["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(value)) {
            throw new ArgumentException("JWT token is required.", nameof(token));
        }

        return value;
    }
}
