namespace CloudyWing.McpLab.Shared.Configuration;

/// <summary>
/// Connection schemas for every MCP service. This is the single definition used for parsing,
/// validation, status reporting, and editing tools.
/// </summary>
public static class ConnectionSchemas {
    private static readonly ConnectionField Name = new(
        ConnectionSchema.NameField,
        ConnectionFieldKind.Text,
        "工具使用的連線名稱，未填時使用 Alias 的小寫"
    );

    /// <summary>SQL Server.</summary>
    public static readonly ConnectionSchema SqlServer = new("mcp-sql-server", "MSSQL_CONN", [
        Name,
        new("HOST", ConnectionFieldKind.Text, "主機名稱或 IP", Required: true),
        new("PORT", ConnectionFieldKind.Port, "連接埠", DefaultValue: "1433"),
        new("USER", ConnectionFieldKind.Text, "SQL 驗證帳號", Required: true),
        new("PASSWORD", ConnectionFieldKind.Secret, "SQL 驗證密碼", Required: true),
        new("DATABASE", ConnectionFieldKind.Text, "預設資料庫", DefaultValue: "master"),
    ]);

    /// <summary>Oracle.</summary>
    public static readonly ConnectionSchema Oracle = new("mcp-oracle", "ORACLE_CONN", [
        Name,
        new("HOST", ConnectionFieldKind.Text, "主機名稱或 IP", Required: true),
        new("PORT", ConnectionFieldKind.Port, "連接埠", DefaultValue: "1521"),
        new("SERVICE", ConnectionFieldKind.Text, "Service name", Required: true),
        new("USER", ConnectionFieldKind.Text, "帳號", Required: true),
        new("PASSWORD", ConnectionFieldKind.Secret, "密碼", Required: true),
    ]);

    /// <summary>Elasticsearch.</summary>
    public static readonly ConnectionSchema Elasticsearch = new("mcp-elasticsearch", "ES_CONN", [
        Name,
        new("URL", ConnectionFieldKind.Url, "叢集 URL，例如 http://host:9200", Required: true),
        new("USER", ConnectionFieldKind.Text, "Basic auth 帳號，未啟用驗證時留空"),
        new("PASSWORD", ConnectionFieldKind.Secret, "Basic auth 密碼"),
        new("SSL_SKIP_VERIFY", ConnectionFieldKind.Bool, "略過 HTTPS 憑證驗證，僅用於自簽憑證", DefaultValue: "false"),
    ]);

    /// <summary>Redis.</summary>
    public static readonly ConnectionSchema Redis = new("mcp-redis", "REDIS_CONN", [
        Name,
        new("HOST", ConnectionFieldKind.Text, "主機名稱或 IP", Required: true),
        new("PORT", ConnectionFieldKind.Port, "連接埠", DefaultValue: "6379"),
        new("PASSWORD", ConnectionFieldKind.Secret, "密碼，未設定 requirepass 時留空"),
        new("DATABASE", ConnectionFieldKind.NonNegativeInt, "資料庫編號", DefaultValue: "0"),
    ]);

    /// <summary>OIDC.</summary>
    public static readonly ConnectionSchema Oidc = new("mcp-oidc", "OIDC_CONN", [
        Name,
        new("ISSUER", ConnectionFieldKind.Url, "Issuer URL", Required: true),
        new("DISCOVERY_URL", ConnectionFieldKind.Url, "Discovery document URL，未填時使用 issuer 的 .well-known", Aliases: ["METADATA_URL"]),
        new("AUDIENCE", ConnectionFieldKind.Text, "驗證 JWT 時預期的 audience"),
        new("REQUIRE_HTTPS_METADATA", ConnectionFieldKind.Bool, "要求 discovery 與 JWKS 使用 HTTPS", DefaultValue: "true"),
    ]);

    /// <summary>MQTT (Mosquitto).</summary>
    public static readonly ConnectionSchema Mqtt = new("mcp-mosquitto", "MQTT_CONN", [
        Name,
        new("HOST", ConnectionFieldKind.Text, "主機名稱或 IP", Required: true),
        new("PORT", ConnectionFieldKind.Port, "連接埠", DefaultValue: "1883"),
        new("USER", ConnectionFieldKind.Text, "帳號，允許匿名時留空"),
        new("PASSWORD", ConnectionFieldKind.Secret, "密碼"),
    ]);

    /// <summary>RabbitMQ.</summary>
    public static readonly ConnectionSchema RabbitMq = new("mcp-rabbitmq", "RABBITMQ_CONN", [
        Name,
        new("HOST", ConnectionFieldKind.Text, "主機名稱或 IP", Required: true),
        new("MGMTPORT", ConnectionFieldKind.Port, "Management API 連接埠", DefaultValue: "15672", Aliases: ["MGMT_PORT"]),
        new("USER", ConnectionFieldKind.Text, "Management API 帳號", Required: true),
        new("PASSWORD", ConnectionFieldKind.Secret, "Management API 密碼", Required: true),
    ]);

    /// <summary>Mailpit.</summary>
    public static readonly ConnectionSchema Mailpit = new("mcp-mailpit", "MAILPIT_CONN", [
        Name,
        new("URL", ConnectionFieldKind.Url, "Mailpit URL，例如 http://host:8025", Required: true),
    ]);

    /// <summary>API Contract.</summary>
    public static readonly ConnectionSchema ApiContract = new(
        "mcp-api-contract",
        "API_CONTRACT_CONN",
        [
            Name,
            new("SPEC_URL", ConnectionFieldKind.Url, "OpenAPI spec URL"),
            new("SPEC_PATH", ConnectionFieldKind.Text, "容器內的 OpenAPI spec 路徑"),
            new("BASE_URL", ConnectionFieldKind.Url, "API base URL，未填時使用 spec 的第一個 server"),
            new("INVOKE_ENABLED", ConnectionFieldKind.Bool, "允許 invoke_endpoint 呼叫 API", DefaultValue: "false"),
            new("ALLOWED_METHODS", ConnectionFieldKind.List, "invoke_endpoint 允許的 HTTP method", DefaultValue: "GET,HEAD,OPTIONS"),
            new("SSL_SKIP_VERIFY", ConnectionFieldKind.Bool, "略過 HTTPS 憑證驗證", DefaultValue: "false"),
            new("AUTH_HEADER_NAME", ConnectionFieldKind.Text, "固定附加的 auth header 名稱"),
            new("AUTH_HEADER_VALUE", ConnectionFieldKind.Secret, "固定附加的 auth header 值"),
        ],
        RequireAnyOf: [["SPEC_URL", "SPEC_PATH"]]
    );

    /// <summary>
    /// Gets every schema.
    /// </summary>
    public static IReadOnlyList<ConnectionSchema> All { get; } = [
        ApiContract,
        SqlServer,
        Oracle,
        Elasticsearch,
        Redis,
        Oidc,
        Mqtt,
        RabbitMq,
        Mailpit,
    ];
}
