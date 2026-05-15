using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Maps health endpoints for HTTP MCP servers.
/// </summary>
public static class McpHealthEndpoint {
    /// <summary>
    /// Maps a minimal health endpoint.
    /// </summary>
    public static void MapMcpHealth(this WebApplication app, string serviceName) {
        app.MapGet("/health", () => Results.Json(new {
            status = "ok",
            service = serviceName,
        }));
    }
}
