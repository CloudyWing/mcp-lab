using System.Text.Json.Nodes;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CloudyWing.McpLab.Shared.Configuration;

/// <summary>
/// Maps <c>/config/status</c>, which reports what the service actually loaded from its config file.
/// The response never contains setting values, only names, states, and reasons.
/// </summary>
public static class ConfigStatusEndpoint {
    /// <summary>
    /// Path of the endpoint.
    /// </summary>
    public const string Path = "/config/status";

    /// <summary>
    /// Maps the endpoint for a service with connections, and loads the registry immediately.
    /// </summary>
    /// <typeparam name="TRegistry">The service's connection registry.</typeparam>
    /// <param name="app">The application.</param>
    /// <param name="serviceName">Compose service name.</param>
    /// <param name="limits">Returns the effective runtime limits, or <see langword="null"/> when the service has none.</param>
    public static void MapConfigStatus<TRegistry>(
        this WebApplication app,
        string serviceName,
        Func<IServiceProvider, object?>? limits = null
    ) where TRegistry : IConnectionStatusSource {
        // Load connections at startup so a later invalid edit can fall back to the last valid settings.
        _ = app.Services.GetRequiredService<TRegistry>();

        app.MapGet(Path, (IServiceProvider services) => Results.Json(Build(
            services,
            serviceName,
            services.GetRequiredService<TRegistry>().Status,
            limits
        )));
    }

    /// <summary>
    /// Maps the endpoint for a service without connections.
    /// </summary>
    public static void MapConfigStatus(
        this WebApplication app,
        string serviceName,
        Func<IServiceProvider, object?>? limits = null
    ) {
        app.MapGet(Path, (IServiceProvider services) => Results.Json(Build(services, serviceName, null, limits)));
    }

    /// <summary>
    /// Extracts the <c>data</c> object of a <see cref="ToolResponse"/> JSON string, such as the output of a
    /// <c>list_runtime_limits</c> tool, so the endpoint reports the same values the tool does.
    /// </summary>
    public static JsonNode? ToolData(string toolResponseJson) => JsonNode.Parse(toolResponseJson)?["data"];

    private static object Build(
        IServiceProvider services,
        string serviceName,
        ConnectionRegistryStatus? status,
        Func<IServiceProvider, object?>? limits
    ) {
        DotEnvSnapshotProvider? snapshot = services.GetService<DotEnvSnapshotProvider>();

        return new {
            service = serviceName,
            config_file = snapshot is null ? null : new {
                path = snapshot.FilePath,
                exists = File.Exists(snapshot.FilePath),
                last_loaded_at = snapshot.LastLoadedAt,
                last_error = snapshot.LastError,
            },
            connections = status?.Connections.Select(item => new {
                alias = item.Alias,
                name = item.Name,
                state = item.State,
                errors = item.Errors,
            }),
            ignored_settings = status?.Ignored.Select(item => new {
                key = item.Key,
                reason = item.Reason,
            }),
            connections_loaded_at = status?.LoadedAt,
            limits = limits?.Invoke(services),
            schema = status is null ? null : DescribeSchema(status.Schema),
        };
    }

    private static object DescribeSchema(ConnectionSchema schema) => new {
        prefix = schema.Prefix,
        variable_pattern = $"{schema.Prefix}_<ALIAS>_<FIELD>",
        fields = schema.Fields.Select(field => new {
            name = field.Name,
            kind = field.Kind.ToString().ToLowerInvariant(),
            required = field.Required,
            default_value = field.DefaultValue,
            description = field.Description,
            aliases = field.Aliases ?? [],
        }),
        require_any_of = schema.RequireAnyOf ?? [],
    };
}
