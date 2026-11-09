using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.SqlServer;

/// <summary>
/// Provides MCP tools for querying and writing to SQL Server databases.
/// </summary>
[McpServerToolType]
public sealed class SqlServerTools {
    private readonly ConnectionRegistry registry;
    private readonly IDotEnvSnapshotProvider snapshotProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="SqlServerTools"/>.
    /// </summary>
    /// <param name="registry">The connection registry.</param>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    public SqlServerTools(ConnectionRegistry registry, IDotEnvSnapshotProvider snapshotProvider) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        this.registry = registry;
        this.snapshotProvider = snapshotProvider;
    }

    private int QueryTimeout => ServiceSettings.SqlServer.QueryTimeout.Read(snapshotProvider.Current);

    private int MaxCellChars => ServiceSettings.SqlServer.MaxCellChars.Read(snapshotProvider.Current);

    private int MaxRows => ServiceSettings.SqlServer.MaxRows.Read(snapshotProvider.Current);

    private int MaxResponseBytes => ServiceSettings.Common.MaxResponseBytes.Read(snapshotProvider.Current);

    /// <summary>
    /// 列出所有已設定的 SQL Server 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 SQL Server 連線")]
    public string ListConnections() =>
        ToolResponse.Ok(
            registry.All.Select(kv => new {
                name = kv.Key,
                environment = registry.EnvironmentOf(kv.Key),
                host = kv.Value.Host,
                port = kv.Value.Port,
                user = kv.Value.User,
                default_database = kv.Value.Database,
            })
        );

    /// <summary>
    /// 列出目前生效的 SQL Server runtime limit 設定。
    /// </summary>
    [McpServerTool, Description("列出目前生效的 SQL Server runtime limit 設定")]
    public string ListRuntimeLimits() =>
        ToolResponse.Ok(new {
            query_timeout = QueryTimeout,
            max_rows = MaxRows,
            max_cell_chars = MaxCellChars,
            max_response_bytes = MaxResponseBytes,
        });

    /// <summary>
    /// 測試指定連線是否正常（回傳伺服器版本）
    /// </summary>
    [McpServerTool, Description("測試指定連線是否正常（回傳伺服器版本）")]
    public async Task<string> PingConnection(
        [Description("連線名稱，省略則使用第一個")] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        try {
            await using SqlConnection conn = await registry.OpenAsync(connection, null, cancellationToken).ConfigureAwait(false);
            using SqlCommand cmd = new("SELECT @@VERSION", conn) { CommandTimeout = 10 };
            string ver = (await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString() ?? "";

            return ToolResponse.Ok(new {
                version = ver[..Math.Min(120, ver.Length)],
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 列出指定連線上所有 ONLINE 的資料庫
    /// </summary>
    [McpServerTool, Description("列出指定連線上所有 ONLINE 的資料庫")]
    public async Task<string> ListDatabases(
        [Description("連線名稱")] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        try {
            await using SqlConnection conn = await registry.OpenAsync(connection, null, cancellationToken).ConfigureAwait(false);
            using SqlCommand cmd = new(
                "SELECT name FROM sys.databases WHERE state_desc='ONLINE' ORDER BY name",
                conn
            ) {
                CommandTimeout = QueryTimeout
            };
            await using SqlDataReader rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            List<string> names = [];

            while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false)) {
                names.Add(rdr.GetString(0));
            }

            return names.Count > 0
                ? ToolResponse.Ok(names)
                : ToolResponse.Empty("No databases found.", names);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 列出指定資料庫中的所有使用者資料表
    /// </summary>
    [McpServerTool, Description("列出指定資料庫中的所有使用者資料表")]
    public async Task<string> ListTables(
        [Description("資料庫名稱，預設 master")] string database = "master",
        [Description("連線名稱")] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        try {
            await using SqlConnection conn = await registry.OpenAsync(connection, database, cancellationToken).ConfigureAwait(false);
            using SqlCommand cmd = new(@"
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE='BASE TABLE'
                ORDER BY TABLE_SCHEMA, TABLE_NAME",
                conn
            ) {
                CommandTimeout = QueryTimeout
            };
            await using SqlDataReader rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            List<string> rows = [];

            while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false)) {
                rows.Add($"{rdr[0]}.{rdr[1]}");
            }

            return rows.Count > 0
                ? ToolResponse.Ok(rows)
                : ToolResponse.Empty("No tables found.", rows);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 描述資料表欄位結構（名稱、型別、可否 NULL）
    /// </summary>
    [McpServerTool, Description("描述資料表欄位結構（名稱、型別、可否 NULL）")]
    public async Task<string> DescribeTable(
        [Description("資料表名稱")] string table,
        [Description("資料庫名稱，預設 master")] string database = "master",
        [Description("Schema 名稱，預設 dbo")] string schema = "dbo",
        [Description("連線名稱")] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        try {
            await using SqlConnection conn = await registry.OpenAsync(connection, database, cancellationToken).ConfigureAwait(false);
            using SqlCommand cmd = new(@"
                SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA=@s AND TABLE_NAME = @t
                ORDER BY ORDINAL_POSITION",
                conn
            ) {
                CommandTimeout = QueryTimeout
            };
            cmd.Parameters.AddWithValue("@s", schema);
            cmd.Parameters.AddWithValue("@t", table);

            await using SqlDataReader rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            List<string> lines = [];

            while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false)) {
                string typeName = rdr.GetString(1);

                if (!rdr.IsDBNull(3)) {
                    typeName += $"({rdr[3]})";
                } else if (!rdr.IsDBNull(4)) {
                    typeName += $"({rdr[4]},{rdr[5]})";
                }

                lines.Add($"{rdr[0]}: {typeName} {(rdr.GetString(2) == "YES" ? "NULL" : "NOT NULL")}");
            }

            return lines.Count > 0
                ? ToolResponse.Ok(lines)
                : ToolResponse.Empty("Table not found.", lines);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 執行唯讀 SQL 查詢（僅允許 SELECT / WITH）
    /// </summary>
    [McpServerTool, Description("執行唯讀 SQL 查詢（僅允許 SELECT / WITH）")]
    public async Task<string> Query(
        [Description("SQL 查詢（SELECT 或 WITH 開頭）")] string sql,
        [Description("資料庫名稱")] string database = "master",
        [Description("連線名稱")] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        return await RunQueryAsync(sql, database, connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 執行唯讀 SQL 查詢（Base64）。避免傳輸過程清洗字元。
    /// </summary>
    [McpServerTool, Description(
        "執行唯讀 SQL 查詢（Base64）。避免傳輸過程清洗字元。" +
        "當字串常值、JSON 等內容被清洗（例如單引號消失）時，請改用本工具。")]
    public async Task<string> QueryBase64(
        [Description("Base64 編碼的 SQL（SELECT 或 WITH 開頭）")] string sqlBase64,
        [Description("資料庫名稱")] string database = "master",
        [Description("連線名稱")] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        if (!Base64Payload.TryDecodeUtf8(
            sqlBase64,
            "SQL payload is empty.",
            "Decoded SQL is empty.",
            out string sql,
            out string error
        )) {
            return ToolResponse.Error(error);
        }

        return await RunQueryAsync(sql, database, connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RunQueryAsync(string sql, string database, string connection, CancellationToken cancellationToken) {
        string upper = sql.TrimStart().ToUpperInvariant();

        if (!upper.StartsWith("SELECT") && !upper.StartsWith("WITH")) {
            return ToolResponse.Error("僅允許 SELECT / WITH 查詢。");
        }

        try {
            SqlGuard.ValidateReadOnlyQuery(sql);
            SqlGuard.Validate(sql);
            await using SqlConnection conn = await registry.OpenAsync(connection, database, cancellationToken).ConfigureAwait(false);
            using SqlCommand cmd = new(sql, conn) { CommandTimeout = QueryTimeout };
            await using SqlDataReader rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            return SqlResultSetReader.ToResponse(await ReadResultSetAsync(rdr, cancellationToken).ConfigureAwait(false));
        } catch (InvalidOperationException ex) {
            return ToolResponse.Blocked(ex.Message);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 執行 SQL（INSERT / UPDATE / DELETE / DDL）。
    /// </summary>
    [McpServerTool, Description(
        "執行 SQL（INSERT / UPDATE / DELETE / DDL）。" +
        "禁止 DROP TABLE、TRUNCATE；DELETE/UPDATE 必須附帶有意義的 WHERE 條件。")]
    public async Task<string> Execute(
        [Description("SQL 語句")] string sql,
        [Description("資料庫名稱")] string database = "master",
        [Description(WriteConnectionGuard.ParameterDescription)] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        return await RunExecuteAsync(sql, database, connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 執行 SQL（Base64）。避免傳輸過程清洗字元。
    /// </summary>
    [McpServerTool, Description(
        "執行 SQL（Base64）。避免傳輸過程清洗字元。" +
        "當字串常值、JSON 等內容被清洗（例如單引號消失）時，請改用本工具。" +
        "禁止 DROP TABLE、TRUNCATE；DELETE/UPDATE 必須附帶有意義的 WHERE 條件。")]
    public async Task<string> ExecuteBase64(
        [Description("Base64 編碼的 SQL")] string sqlBase64,
        [Description("資料庫名稱")] string database = "master",
        [Description(WriteConnectionGuard.ParameterDescription)] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        if (!Base64Payload.TryDecodeUtf8(
            sqlBase64,
            "SQL payload is empty.",
            "Decoded SQL is empty.",
            out string sql,
            out string error
        )) {
            return ToolResponse.Error(error);
        }

        return await RunExecuteAsync(sql, database, connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RunExecuteAsync(string sql, string database, string connection, CancellationToken cancellationToken) {
        if (WriteConnectionGuard.CheckMissing(connection) is string blocked) {
            return blocked;
        }

        try {
            SqlGuard.Validate(sql);
            await using SqlConnection conn = await registry.OpenAsync(connection, database, cancellationToken).ConfigureAwait(false);
            using SqlCommand cmd = new(sql, conn) {
                CommandTimeout = QueryTimeout
            };
            string upper = sql.TrimStart().ToUpperInvariant();

            if (upper.StartsWith("SELECT") || upper.StartsWith("WITH")) {
                await using SqlDataReader rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                return SqlResultSetReader.ToResponse(await ReadResultSetAsync(rdr, cancellationToken).ConfigureAwait(false));
            }

            int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return ToolResponse.Ok(new {
                affected_rows = affected >= 0 ? (int?)affected : null,
            }, affected >= 0 ? "Command affected rows." : "Command executed successfully.");
        } catch (InvalidOperationException ex) {
            return ToolResponse.Blocked(ex.Message);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    private Task<object> ReadResultSetAsync(SqlDataReader rdr, CancellationToken cancellationToken) =>
        SqlResultSetReader.ReadAsync(rdr, MaxRows, MaxCellChars, MaxResponseBytes, cancellationToken);
}
