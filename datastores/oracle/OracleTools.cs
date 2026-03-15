namespace CloudyWing.McpLab.Oracle;

[McpServerToolType]
/// <summary>
/// Provides MCP tools for querying and writing to Oracle databases.
/// </summary>
public sealed class OracleTools {
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly ConnectionRegistry registry;

    /// <summary>
    /// Initializes a new instance of <see cref="OracleTools"/> with the specified connection registry.
    /// </summary>
    public OracleTools(ConnectionRegistry registry) {
        this.registry = registry;
    }

    private static int QueryTimeout =>
        int.TryParse(Environment.GetEnvironmentVariable("QUERY_TIMEOUT"), out int timeoutSeconds)
            ? timeoutSeconds : 60;

    private static int MaxRows =>
        int.TryParse(Environment.GetEnvironmentVariable("MAX_ROWS"), out int maxRows)
            ? maxRows : 500;

    /// <summary>
    /// 列出所有已設定的 Oracle 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 Oracle 連線")]
    public string ListConnections() =>
        JsonSerializer.Serialize(
            registry.All.Select(
                kv => new {
                    name = kv.Key,
                    host = kv.Value.Host,
                    port = kv.Value.Port,
                    service = kv.Value.Service,
                    user = kv.Value.User,
                }
            ), Json
        );

    /// <summary>
    /// 測試 Oracle 連線是否正常
    /// </summary>
    [McpServerTool, Description("測試 Oracle 連線是否正常")]
    public string PingConnection(
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            using OracleConnection conn = registry.Open(connection);
            using OracleCommand cmd = new("SELECT banner FROM v$version WHERE ROWNUM = 1", conn) {
                CommandTimeout = 10
            };
            string ver = cmd.ExecuteScalar()?.ToString() ?? "";
            return $"OK: {ver[..Math.Min(120, ver.Length)]}";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 列出 Oracle 資料表（owner 省略則顯示目前使用者的）
    /// </summary>
    [McpServerTool, Description("列出 Oracle 資料表（owner 省略則顯示目前使用者的）")]
    public string ListTables(
        [Description("Schema 擁有者，省略則列出當前使用者的資料表")] string owner = "",
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using OracleConnection conn = registry.Open(connection);
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

            using OracleDataReader rdr = cmd.ExecuteReader();
            List<string> rows = [];

            while (rdr.Read()) {
                rows.Add(rdr.FieldCount == 2 ? $"{rdr[0]}.{rdr[1]}" : rdr.GetString(0));
            }

            return rows.Count > 0 ? string.Join("\n", rows) : "No tables found.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 描述 Oracle 資料表欄位結構
    /// </summary>
    [McpServerTool, Description("描述 Oracle 資料表欄位結構")]
    public string DescribeTable(
        [Description("資料表名稱")] string table,
        [Description("Schema 擁有者，省略則查詢當前使用者")] string owner = "",
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using OracleConnection conn = registry.Open(connection);
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

            using OracleDataReader rdr = cmd.ExecuteReader();
            List<string> lines = [];

            while (rdr.Read()) {
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
                ? string.Join("\n", lines) : "Table not found.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 執行唯讀 Oracle SQL 查詢（僅允許 SELECT / WITH）
    /// </summary>
    [McpServerTool, Description("執行唯讀 Oracle SQL 查詢（僅允許 SELECT / WITH）")]
    public string Query(
        [Description("SQL 查詢（SELECT 或 WITH 開頭）")] string sql,
        [Description("連線名稱")] string connection = ""
    ) {
        return RunQuery(sql, connection);
    }

    /// <summary>
    /// 執行唯讀 Oracle SQL 查詢（Base64）。避免傳輸過程清洗字元。
    /// </summary>
    [McpServerTool, Description(
        "執行唯讀 Oracle SQL 查詢（Base64）。避免傳輸過程清洗字元。" +
        "當字串常值、JSON 等內容被清洗（例如單引號消失）時，請改用本工具。")]
    public string QueryBase64(
        [Description("Base64 編碼的 SQL（SELECT 或 WITH 開頭）")] string sqlBase64,
        [Description("連線名稱")] string connection = ""
    ) {
        if (!TryDecodeBase64(sqlBase64, out string sql, out string error)) {
            return $"Error: {error}";
        }

        return RunQuery(sql, connection);
    }

    private string RunQuery(string sql, string connection) {
        string upper = sql.TrimStart().ToUpperInvariant();

        if (!upper.StartsWith("SELECT") && !upper.StartsWith("WITH")) {
            return "Error: 僅允許 SELECT / WITH 查詢。";
        }

        try {
            SqlGuard.Validate(sql);
            using OracleConnection conn = registry.Open(connection);
            using OracleCommand cmd = new(sql, conn) { CommandTimeout = QueryTimeout };
            using OracleDataReader rdr = cmd.ExecuteReader();

            return FormatResultSet(rdr);
        } catch (InvalidOperationException ex) {
            return $"Blocked: {ex.Message}";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("執行 Oracle SQL / PL/SQL（INSERT / UPDATE / DELETE / DDL / ANONYMOUS BLOCK）。禁止 DROP TABLE、TRUNCATE；DELETE/UPDATE 必須附帶有意義的 WHERE 條件。")]
    public string Execute(
        [Description("SQL 或 PL/SQL 語句")] string sql,
        [Description("連線名稱")] string connection = ""
    ) {
        return RunExecute(sql, connection);
    }

    [McpServerTool]
    [Description(
        "執行 Oracle SQL / PL/SQL（Base64）。避免傳輸過程清洗字元。" +
        "當字串常值、JSON 等內容被清洗（例如單引號消失）時，請改用本工具。" +
        "禁止 DROP TABLE、TRUNCATE；DELETE/UPDATE 必須附帶有意義的 WHERE 條件。")]
    public string ExecuteBase64(
        [Description("Base64 編碼的 SQL 或 PL/SQL")] string sqlBase64,
        [Description("連線名稱")] string connection = ""
    ) {
        if (!TryDecodeBase64(sqlBase64, out string sql, out string error)) {
            return $"Error: {error}";
        }

        return RunExecute(sql, connection);
    }

    private string RunExecute(string sql, string connection) {
        try {
            SqlGuard.Validate(sql);
            using OracleConnection conn = registry.Open(connection);
            using OracleCommand cmd = new(sql, conn) { CommandTimeout = QueryTimeout };
            string upper = sql.TrimStart().ToUpperInvariant();

            if (upper.StartsWith("SELECT") || upper.StartsWith("WITH")) {
                using OracleDataReader rdr = cmd.ExecuteReader();
                return FormatResultSet(rdr);
            }

            int affected = cmd.ExecuteNonQuery();

            return affected >= 0
                ? $"{affected} row(s) affected." : "Command executed successfully.";
        } catch (InvalidOperationException ex) {
            return $"Blocked: {ex.Message}";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    private static bool TryDecodeBase64(string input, out string sql, out string error) {
        sql = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input)) {
            error = "SQL payload is empty.";
            return false;
        }

        try {
            string normalized = NormalizeBase64(input);
            byte[] bytes = Convert.FromBase64String(normalized);
            sql = Encoding.UTF8.GetString(bytes);

            if (string.IsNullOrWhiteSpace(sql)) {
                error = "Decoded SQL is empty.";
                return false;
            }

            return true;
        } catch (FormatException) {
            error = "Invalid Base64.";
            return false;
        }
    }

    private static string NormalizeBase64(string input) {
        string value = input.Trim()
            .Replace('-', '+')
            .Replace('_', '/');
        int mod = value.Length % 4;

        if (mod == 2) {
            return value + "==";
        }

        if (mod == 3) {
            return value + "=";
        }

        return value;
    }

    private static string FormatResultSet(OracleDataReader rdr) {
        StringBuilder sb = new();
        int max = MaxRows;
        string[] cols = Enumerable.Range(0, rdr.FieldCount)
            .Select(i => rdr.GetName(i))
            .ToArray();

        sb.AppendLine(string.Join(" | ", cols));
        sb.AppendLine(string.Join(" | ", cols.Select(_ => "---")));

        int count = 0;

        while (rdr.Read() && count < max) {
            sb.AppendLine(string.Join(" | ",
                Enumerable.Range(0, rdr.FieldCount)
                    .Select(i => rdr.IsDBNull(i) ? "NULL" : rdr[i]?.ToString() ?? "")
            ));
            count++;
        }

        if (count == max) {
            sb.Append($"... (truncated to {max} rows)");
        }

        return sb.ToString().TrimEnd();
    }
}
