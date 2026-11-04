using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.Oracle;

/// <summary>
/// Provides MCP tools for querying and writing to Oracle databases.
/// </summary>
[McpServerToolType]
public sealed class OracleTools {
    private readonly ConnectionRegistry registry;
    private readonly IDotEnvSnapshotProvider snapshotProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="OracleTools"/>.
    /// </summary>
    /// <param name="registry">The connection registry.</param>
    /// <param name="snapshotProvider">The .env snapshot provider.</param>
    public OracleTools(ConnectionRegistry registry, IDotEnvSnapshotProvider snapshotProvider) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        this.registry = registry;
        this.snapshotProvider = snapshotProvider;
    }

    private int QueryTimeout => ServiceSettings.Oracle.QueryTimeout.Read(snapshotProvider.Current);

    private int MaxCellChars => ServiceSettings.Oracle.MaxCellChars.Read(snapshotProvider.Current);

    private int MaxRows => ServiceSettings.Oracle.MaxRows.Read(snapshotProvider.Current);

    /// <summary>
    /// 列出所有已設定的 Oracle 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 Oracle 連線")]
    public string ListConnections() =>
        ToolResponse.Ok(
            registry.All.Select(
                kv => new {
                    name = kv.Key,
                environment = registry.EnvironmentOf(kv.Key),
                    host = kv.Value.Host,
                    port = kv.Value.Port,
                    service = kv.Value.Service,
                    user = kv.Value.User,
                }
            )
        );

    /// <summary>
    /// 列出目前生效的 Oracle runtime limit 設定。
    /// </summary>
    [McpServerTool, Description("列出目前生效的 Oracle runtime limit 設定")]
    public string ListRuntimeLimits() =>
        ToolResponse.Ok(new {
            query_timeout = QueryTimeout,
            max_rows = MaxRows,
            max_cell_chars = MaxCellChars,
        });

    /// <summary>
    /// 測試 Oracle 連線是否正常
    /// </summary>
    [McpServerTool, Description("測試 Oracle 連線是否正常")]
    public async Task<string> PingConnection(
        [Description("連線名稱，省略則使用第一個")] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        try {
            await using OracleConnection conn = await registry.OpenAsync(connection, cancellationToken).ConfigureAwait(false);
            using OracleCommand cmd = new("SELECT banner FROM v$version WHERE ROWNUM = 1", conn) {
                CommandTimeout = 10
            };
            string ver = (await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString() ?? "";
            return ToolResponse.Ok(new {
                version = ver[..Math.Min(120, ver.Length)],
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 列出 Oracle 資料表（owner 省略則顯示目前使用者的）
    /// </summary>
    [McpServerTool, Description("列出 Oracle 資料表（owner 省略則顯示目前使用者的）")]
    public async Task<string> ListTables(
        [Description("Schema 擁有者，省略則列出當前使用者的資料表")] string owner = "",
        [Description("連線名稱")] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        try {
            await using OracleConnection conn = await registry.OpenAsync(connection, cancellationToken).ConfigureAwait(false);
            using OracleCommand cmd = conn.CreateCommand();
            cmd.CommandTimeout = QueryTimeout;

            if (!string.IsNullOrWhiteSpace(owner)) {
                cmd.CommandText =
                    "SELECT owner, table_name FROM all_tables " +
                    "WHERE owner=:o ORDER BY table_name";
                cmd.Parameters.Add("o", owner.ToUpperInvariant());
            } else {
                cmd.CommandText = "SELECT table_name FROM user_tables ORDER BY table_name";
            }

            await using OracleDataReader rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            List<string> rows = [];

            while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false)) {
                rows.Add(rdr.FieldCount == 2 ? $"{rdr[0]}.{rdr[1]}" : rdr.GetString(0));
            }

            return rows.Count > 0
                ? ToolResponse.Ok(rows)
                : ToolResponse.Empty("No tables found.", rows);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 描述 Oracle 資料表欄位結構
    /// </summary>
    [McpServerTool, Description("描述 Oracle 資料表欄位結構")]
    public async Task<string> DescribeTable(
        [Description("資料表名稱")] string table,
        [Description("Schema 擁有者，省略則查詢當前使用者")] string owner = "",
        [Description("連線名稱")] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        try {
            await using OracleConnection conn = await registry.OpenAsync(connection, cancellationToken).ConfigureAwait(false);
            using OracleCommand cmd = conn.CreateCommand();
            cmd.CommandTimeout = QueryTimeout;

            if (!string.IsNullOrWhiteSpace(owner)) {
                cmd.CommandText = @"
                    SELECT column_name, data_type, nullable, data_length, data_precision, data_scale
                    FROM all_tab_columns WHERE owner=:o AND table_name=:t
                    ORDER BY column_id";
                cmd.Parameters.Add("o", owner.ToUpperInvariant());
                cmd.Parameters.Add("t", table.ToUpperInvariant());
            } else {
                cmd.CommandText = @"
                    SELECT column_name, data_type, nullable, data_length, data_precision, data_scale
                    FROM user_tab_columns WHERE table_name=:t
                    ORDER BY column_id";
                cmd.Parameters.Add("t", table.ToUpperInvariant());
            }

            await using OracleDataReader rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            List<string> lines = [];

            while (await rdr.ReadAsync(cancellationToken).ConfigureAwait(false)) {
                string typeName = rdr.GetString(1);

                if (!rdr.IsDBNull(4)) {
                    typeName += $"({rdr[4]},{(rdr.IsDBNull(5) ? "0" : rdr[5])})";
                } else if (!rdr.IsDBNull(3)) {
                    typeName += $"({rdr[3]})";
                }

                string line = $"{rdr[0]}: {typeName} {(rdr.GetString(2) == "Y" ? "NULL" : "NOT NULL")}";
                lines.Add(line);
            }

            return lines.Count > 0
                ? ToolResponse.Ok(lines)
                : ToolResponse.Empty("Table not found.", lines);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 執行唯讀 Oracle SQL 查詢（僅允許 SELECT / WITH）
    /// </summary>
    [McpServerTool, Description("執行唯讀 Oracle SQL 查詢（僅允許 SELECT / WITH）")]
    public async Task<string> Query(
        [Description("SQL 查詢（SELECT 或 WITH 開頭）")] string sql,
        [Description("連線名稱")] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        return await RunQueryAsync(sql, connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 執行唯讀 Oracle SQL 查詢（Base64）。避免傳輸過程清洗字元。
    /// </summary>
    [McpServerTool, Description(
        "執行唯讀 Oracle SQL 查詢（Base64）。避免傳輸過程清洗字元。" +
        "當字串常值、JSON 等內容被清洗（例如單引號消失）時，請改用本工具。")]
    public async Task<string> QueryBase64(
        [Description("Base64 編碼的 SQL（SELECT 或 WITH 開頭）")] string sqlBase64,
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

        return await RunQueryAsync(sql, connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RunQueryAsync(string sql, string connection, CancellationToken cancellationToken) {
        string upper = sql.TrimStart().ToUpperInvariant();

        if (!upper.StartsWith("SELECT") && !upper.StartsWith("WITH")) {
            return ToolResponse.Error("僅允許 SELECT / WITH 查詢。");
        }

        try {
            SqlGuard.ValidateReadOnlyQuery(sql);
            SqlGuard.Validate(sql);
            await using OracleConnection conn = await registry.OpenAsync(connection, cancellationToken).ConfigureAwait(false);
            using OracleCommand cmd = new(sql, conn) { CommandTimeout = QueryTimeout };
            await using OracleDataReader rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            return ToolResponse.Ok(await ReadResultSetAsync(rdr, cancellationToken).ConfigureAwait(false));
        } catch (InvalidOperationException ex) {
            return ToolResponse.Blocked(ex.Message);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 執行 Oracle SQL / PL/SQL（INSERT / UPDATE / DELETE / DDL / ANONYMOUS BLOCK）。
    /// </summary>
    [McpServerTool, Description(
        "執行 Oracle SQL / PL/SQL（INSERT / UPDATE / DELETE / DDL / ANONYMOUS BLOCK）。" +
        "禁止 DROP TABLE、TRUNCATE；DELETE/UPDATE 必須附帶有意義的 WHERE 條件。")]
    public async Task<string> Execute(
        [Description("SQL 或 PL/SQL 語句")] string sql,
        [Description(WriteConnectionGuard.ParameterDescription)] string connection = "",
        CancellationToken cancellationToken = default
    ) {
        return await RunExecuteAsync(sql, connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 執行 Oracle SQL / PL/SQL（Base64）。避免傳輸過程清洗字元。
    /// </summary>
    [McpServerTool, Description(
        "執行 Oracle SQL / PL/SQL（Base64）。避免傳輸過程清洗字元。" +
        "當字串常值、JSON 等內容被清洗（例如單引號消失）時，請改用本工具。" +
        "禁止 DROP TABLE、TRUNCATE；DELETE/UPDATE 必須附帶有意義的 WHERE 條件。")]
    public async Task<string> ExecuteBase64(
        [Description("Base64 編碼的 SQL 或 PL/SQL")] string sqlBase64,
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

        return await RunExecuteAsync(sql, connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RunExecuteAsync(string sql, string connection, CancellationToken cancellationToken) {
        if (WriteConnectionGuard.CheckMissing(connection) is string blocked) {
            return blocked;
        }

        try {
            SqlGuard.Validate(sql);
            await using OracleConnection conn = await registry.OpenAsync(connection, cancellationToken).ConfigureAwait(false);
            using OracleCommand cmd = new(sql, conn) { CommandTimeout = QueryTimeout };
            string upper = sql.TrimStart().ToUpperInvariant();

            if (upper.StartsWith("SELECT") || upper.StartsWith("WITH")) {
                await using OracleDataReader rdr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                return ToolResponse.Ok(await ReadResultSetAsync(rdr, cancellationToken).ConfigureAwait(false));
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

    private Task<object> ReadResultSetAsync(OracleDataReader rdr, CancellationToken cancellationToken) =>
        SqlResultSetReader.ReadAsync(rdr, MaxRows, MaxCellChars, cancellationToken);
}
