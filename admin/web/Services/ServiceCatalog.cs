using CloudyWing.McpLab.Shared.Configuration;

namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// An MCP service the admin can manage.
/// </summary>
/// <param name="Name">Compose service name, which is also its config directory name.</param>
/// <param name="DisplayName">Name shown in the UI.</param>
/// <param name="Category">Group shown in the navigation.</param>
/// <param name="Schema">Connection schema, or <see langword="null"/> when the service has no connections.</param>
/// <param name="Settings">Service-level settings.</param>
/// <param name="TestTool">MCP tool used to test a connection; it takes a <c>connection</c> argument.</param>
/// <param name="ExtraKeyPrefixes">Prefixes of other keys that belong to this service, used when importing a .env file.</param>
public sealed record ManagedService(
    string Name,
    string DisplayName,
    string Category,
    ConnectionSchema? Schema,
    IReadOnlyList<ServiceSetting> Settings,
    string? TestTool,
    IReadOnlyList<string> ExtraKeyPrefixes
) {
    /// <summary>
    /// Gets whether a key from a .env file belongs to this service.
    /// </summary>
    public bool Owns(string key) =>
        (Schema is not null && key.StartsWith(Schema.Prefix + "_", StringComparison.Ordinal))
        || Settings.Any(setting => setting.Name == key || (setting.Fallbacks?.Contains(key) ?? false))
        || ExtraKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));
}

/// <summary>
/// The MCP services managed by the admin, built from the shared schema and settings definitions.
/// </summary>
public static class ServiceCatalog {
    /// <summary>
    /// Keys written to every service file.
    /// </summary>
    public static IReadOnlyList<string> SharedKeys { get; } = [
        McpAccessToken.VariableName,
        ServiceSettings.Common.MaxResponseBytes.Name
    ];

    /// <summary>
    /// Gets every managed service, in navigation order.
    /// </summary>
    public static IReadOnlyList<ManagedService> All { get; } = [
        new("mcp-sql-server", "SQL Server", "資料庫", ConnectionSchemas.SqlServer, ServiceSettings.SqlServer.All, "ping_connection", []),
        new("mcp-oracle", "Oracle", "資料庫", ConnectionSchemas.Oracle, ServiceSettings.Oracle.All, "ping_connection", []),
        new("mcp-elasticsearch", "Elasticsearch", "資料庫", ConnectionSchemas.Elasticsearch, ServiceSettings.Elasticsearch.All, "ping_connection", []),
        new("mcp-redis", "Redis", "資料庫", ConnectionSchemas.Redis, ServiceSettings.Redis.All, "ping_connection", []),
        new("mcp-mosquitto", "MQTT", "訊息", ConnectionSchemas.Mqtt, ServiceSettings.Mosquitto.All, "ping_connection", []),
        new("mcp-rabbitmq", "RabbitMQ", "訊息", ConnectionSchemas.RabbitMq, ServiceSettings.RabbitMq.All, "ping_connection", []),
        new("mcp-api-contract", "API Contract", "API 與身分", ConnectionSchemas.ApiContract, ServiceSettings.ApiContract.All, "get_spec_summary", []),
        new("mcp-oidc", "OIDC", "API 與身分", ConnectionSchemas.Oidc, ServiceSettings.Oidc.All, "get_discovery_document", []),
        new("mcp-mailpit", "Mailpit", "工具", ConnectionSchemas.Mailpit, ServiceSettings.Mailpit.All, "ping_connection", []),
        new("mcp-reader", "Reader", "工具", null, [], null, ["DOCUMENT_", "MAX_IMAGES", "MAX_TEXT_MB"]),
        new("mcp-docker", "Docker", "工具", null, ServiceSettings.Docker.All, null, []),
    ];

    /// <summary>
    /// Finds a service by name.
    /// </summary>
    public static ManagedService? Find(string name) =>
        All.FirstOrDefault(service => string.Equals(service.Name, name, StringComparison.OrdinalIgnoreCase));
}
