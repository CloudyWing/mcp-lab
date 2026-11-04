using System.Security.Cryptography;
using System.Text;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Optional bearer token check for MCP endpoints, read from the runtime .env snapshot on every request.
/// </summary>
public static class McpAccessToken {
    /// <summary>
    /// Environment variable holding the expected token. Empty or unset disables the check.
    /// </summary>
    public const string VariableName = "MCPLAB_MCP_TOKEN";

    /// <summary>
    /// Requires <c>Authorization: Bearer &lt;token&gt;</c> on every path except <c>/health</c> when a token is configured.
    /// </summary>
    public static void UseMcpAccessToken(this WebApplication app) {
        IDotEnvSnapshotProvider snapshotProvider = app.Services.GetRequiredService<IDotEnvSnapshotProvider>();

        app.Use(async (context, next) => {
            if (context.Request.Path.StartsWithSegments("/health")) {
                await next(context);
                return;
            }

            string expected = GetExpectedToken(snapshotProvider.Current);

            if (expected.Length == 0 || IsAuthorized(context.Request.Headers.Authorization.ToString(), expected)) {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new {
                error = "unauthorized",
                message = $"Missing or invalid bearer token. Set the Authorization header to match {VariableName}.",
            });
        });
    }

    /// <summary>
    /// Gets the configured token, or an empty string when the check is disabled.
    /// </summary>
    public static string GetExpectedToken(IReadOnlyDictionary<string, string> snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.TryGetValue(VariableName, out string? value) ? value.Trim() : "";
    }

    /// <summary>
    /// Compares an Authorization header value with the expected token in constant time.
    /// </summary>
    public static bool IsAuthorized(string? authorizationHeader, string expected) {
        const string scheme = "Bearer ";

        if (string.IsNullOrEmpty(expected)
            || authorizationHeader is null
            || !authorizationHeader.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        byte[] actualBytes = Encoding.UTF8.GetBytes(authorizationHeader[scheme.Length..].Trim());
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);

        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
