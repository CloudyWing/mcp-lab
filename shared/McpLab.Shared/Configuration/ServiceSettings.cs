namespace CloudyWing.McpLab.Shared.Configuration;

/// <summary>
/// A service-level numeric setting read from the service's config file on every use, so changes apply without a restart.
/// Values that are not integers or are below <see cref="Min"/> fall back to <see cref="DefaultValue"/>;
/// values above <see cref="Max"/> are capped.
/// </summary>
/// <param name="Name">Variable name.</param>
/// <param name="Description">Short description shown to people editing the setting.</param>
/// <param name="Unit">Unit of the value, such as <c>seconds</c> or <c>rows</c>.</param>
/// <param name="DefaultValue">Value used when the setting is empty or invalid.</param>
/// <param name="Min">Smallest accepted value.</param>
/// <param name="Max">Largest accepted value.</param>
/// <param name="Fallbacks">Older variable names read when <see cref="Name"/> is empty.</param>
public sealed record ServiceSetting(
    string Name,
    string Description,
    string Unit,
    int DefaultValue,
    int Min,
    int Max,
    IReadOnlyList<string>? Fallbacks = null
) {
    /// <summary>
    /// Reads the effective value from a snapshot.
    /// </summary>
    public int Read(IReadOnlyDictionary<string, string> snapshot) =>
        ToolRuntimeOptions.GetSnapshotInt32(snapshot, [Name, .. Fallbacks ?? []], DefaultValue, Min, Max);

    /// <summary>
    /// Reads the effective value as a <see cref="TimeSpan"/> of seconds.
    /// </summary>
    public TimeSpan ReadSeconds(IReadOnlyDictionary<string, string> snapshot) => TimeSpan.FromSeconds(Read(snapshot));
}

/// <summary>
/// Service-level settings of every MCP service. This is the single definition used by the tools,
/// <c>/config/status</c>, and editing tools.
/// </summary>
public static class ServiceSettings {
    private static readonly string[] QueryTimeoutFallback = ["QUERY_TIMEOUT"];
    private static readonly string[] MaxRowsFallback = ["MAX_ROWS"];

    /// <summary>Settings shared by every MCP service.</summary>
    public static class Common {
        /// <summary>Largest total content bytes of a single tool response.</summary>
        public static readonly ServiceSetting MaxResponseBytes =
            new("MCPLAB_MAX_RESPONSE_BYTES", "單次工具回應的內容位元組上限", "bytes", 1_048_576, 1, 2_097_152);
    }

    /// <summary>SQL Server settings.</summary>
    public static class SqlServer {
        /// <summary>Query timeout.</summary>
        public static readonly ServiceSetting QueryTimeout =
            new("MSSQL_QUERY_TIMEOUT", "查詢逾時", "seconds", 60, 1, 300, QueryTimeoutFallback);

        /// <summary>Maximum returned rows.</summary>
        public static readonly ServiceSetting MaxRows =
            new("MSSQL_MAX_ROWS", "查詢最大回傳列數", "rows", 500, 1, 5000, MaxRowsFallback);

        /// <summary>Maximum characters per cell.</summary>
        public static readonly ServiceSetting MaxCellChars =
            new("MSSQL_MAX_CELL_CHARS", "單一欄位回傳字元上限", "chars", SqlResultSetReader.DefaultMaxCellChars, 1, SqlResultSetReader.MaxCellCharsLimit);

        /// <summary>All SQL Server settings.</summary>
        public static IReadOnlyList<ServiceSetting> All { get; } = [QueryTimeout, MaxRows, MaxCellChars, Common.MaxResponseBytes];
    }

    /// <summary>Oracle settings.</summary>
    public static class Oracle {
        /// <summary>Query timeout.</summary>
        public static readonly ServiceSetting QueryTimeout =
            new("ORACLE_QUERY_TIMEOUT", "查詢逾時", "seconds", 60, 1, 300, QueryTimeoutFallback);

        /// <summary>Maximum returned rows.</summary>
        public static readonly ServiceSetting MaxRows =
            new("ORACLE_MAX_ROWS", "查詢最大回傳列數", "rows", 500, 1, 5000, MaxRowsFallback);

        /// <summary>Maximum characters per cell.</summary>
        public static readonly ServiceSetting MaxCellChars =
            new("ORACLE_MAX_CELL_CHARS", "單一欄位回傳字元上限", "chars", SqlResultSetReader.DefaultMaxCellChars, 1, SqlResultSetReader.MaxCellCharsLimit);

        /// <summary>All Oracle settings.</summary>
        public static IReadOnlyList<ServiceSetting> All { get; } = [QueryTimeout, MaxRows, MaxCellChars, Common.MaxResponseBytes];
    }

    /// <summary>Elasticsearch settings.</summary>
    public static class Elasticsearch {
        /// <summary>Default search result size.</summary>
        public static readonly ServiceSetting SearchSize =
            new("ES_SEARCH_SIZE", "搜尋預設回傳筆數", "documents", 10, 1, 1000, MaxRowsFallback);

        /// <summary>HTTP request timeout.</summary>
        public static readonly ServiceSetting HttpTimeout =
            new("ES_HTTP_TIMEOUT", "HTTP 請求逾時", "seconds", 30, 1, 300);

        /// <summary>All Elasticsearch settings.</summary>
        public static IReadOnlyList<ServiceSetting> All { get; } = [SearchSize, HttpTimeout, Common.MaxResponseBytes];
    }

    /// <summary>Redis settings.</summary>
    public static class Redis {
        /// <summary>Maximum keys or collection items returned.</summary>
        public static readonly ServiceSetting MaxRows =
            new("REDIS_MAX_ROWS", "列舉 key 與集合項目的最大回傳筆數", "items", 100, 1, 1000, MaxRowsFallback);

        /// <summary>All Redis settings.</summary>
        public static IReadOnlyList<ServiceSetting> All { get; } = [MaxRows, Common.MaxResponseBytes];
    }

    /// <summary>API Contract settings.</summary>
    public static class ApiContract {
        /// <summary>HTTP request timeout.</summary>
        public static readonly ServiceSetting HttpTimeout =
            new("API_CONTRACT_HTTP_TIMEOUT", "HTTP 請求逾時", "seconds", 15, 1, 300);

        /// <summary>Default response body bytes returned by invoke_endpoint.</summary>
        public static readonly ServiceSetting DefaultBodyBytes =
            new("API_CONTRACT_DEFAULT_BODY_BYTES", "invoke_endpoint 預設回傳的 body 位元組數", "bytes", 50000, 1, 10_000_000);

        /// <summary>Largest response body bytes a caller may request.</summary>
        public static readonly ServiceSetting MaxBodyBytes =
            new("API_CONTRACT_MAX_BODY_BYTES", "invoke_endpoint 可要求的 body 位元組上限", "bytes", 200000, 1, 10_000_000);

        /// <summary>All API Contract settings.</summary>
        public static IReadOnlyList<ServiceSetting> All { get; } = [HttpTimeout, DefaultBodyBytes, MaxBodyBytes, Common.MaxResponseBytes];
    }

    /// <summary>OIDC settings.</summary>
    public static class Oidc {
        /// <summary>HTTP request timeout.</summary>
        public static readonly ServiceSetting HttpTimeout =
            new("OIDC_HTTP_TIMEOUT", "讀取 discovery 與 JWKS 的 HTTP 逾時", "seconds", 10, 1, 300);

        /// <summary>How long discovery documents and JWKS are cached.</summary>
        public static readonly ServiceSetting MetadataCacheSeconds =
            new("OIDC_METADATA_CACHE_SECONDS", "Discovery 與 JWKS 的快取秒數，0 表示不快取", "seconds", 300, 0, 86400);

        /// <summary>All OIDC settings.</summary>
        public static IReadOnlyList<ServiceSetting> All { get; } = [HttpTimeout, MetadataCacheSeconds, Common.MaxResponseBytes];
    }

    /// <summary>RabbitMQ settings.</summary>
    public static class RabbitMq {
        /// <summary>HTTP request timeout.</summary>
        public static readonly ServiceSetting HttpTimeout =
            new("RABBITMQ_HTTP_TIMEOUT", "Management API 的 HTTP 逾時", "seconds", 10, 1, 300);

        /// <summary>All RabbitMQ settings.</summary>
        public static IReadOnlyList<ServiceSetting> All { get; } = [HttpTimeout, Common.MaxResponseBytes];
    }

    /// <summary>Mailpit settings.</summary>
    public static class Mailpit {
        /// <summary>HTTP request timeout.</summary>
        public static readonly ServiceSetting HttpTimeout =
            new("MAILPIT_HTTP_TIMEOUT", "Mailpit API 的 HTTP 逾時", "seconds", 10, 1, 300);

        /// <summary>All Mailpit settings.</summary>
        public static IReadOnlyList<ServiceSetting> All { get; } = [HttpTimeout, Common.MaxResponseBytes];
    }

    /// <summary>Mosquitto settings.</summary>
    public static class Mosquitto {
        /// <summary>All Mosquitto settings.</summary>
        public static IReadOnlyList<ServiceSetting> All { get; } = [Common.MaxResponseBytes];
    }

    /// <summary>Docker settings.</summary>
    public static class Docker {
        /// <summary>All Docker settings.</summary>
        public static IReadOnlyList<ServiceSetting> All { get; } = [Common.MaxResponseBytes];
    }

    /// <summary>
    /// Gets the settings of a service by its Compose service name.
    /// </summary>
    public static IReadOnlyList<ServiceSetting> ForService(string service) => service switch {
        "mcp-sql-server" => SqlServer.All,
        "mcp-oracle" => Oracle.All,
        "mcp-elasticsearch" => Elasticsearch.All,
        "mcp-redis" => Redis.All,
        "mcp-api-contract" => ApiContract.All,
        "mcp-oidc" => Oidc.All,
        "mcp-rabbitmq" => RabbitMq.All,
        "mcp-mailpit" => Mailpit.All,
        "mcp-mosquitto" => Mosquitto.All,
        "mcp-docker" => Docker.All,
        _ => [],
    };
}
