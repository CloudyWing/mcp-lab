namespace CloudyWing.McpLab.SqlServer;

/// <summary>
/// Provides MCP tools for querying and writing to SQL Server databases.
/// </summary>
[McpServerToolType]
public sealed class SqlServerTools {
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly ConnectionRegistry registry;

    /// <summary>
    /// Initializes a new instance of <see cref="SqlServerTools"/> with the specified connection registry.
    /// </summary>
    public SqlServerTools(ConnectionRegistry registry) {
        this.registry = registry;
    }

    private static int QueryTimeout =>
        int.TryParse(Environment.GetEnvironmentVariable("QUERY_TIMEOUT"), out int timeoutSeconds)
            ? timeoutSeconds : 60;

    private static int MaxRows =>
        int.TryParse(Environment.GetEnvironmentVariable("MAX_ROWS"), out int maxRows)
            ? maxRows : 500;

    /// <summary>
    /// 列出所有已設定的 SQL Server 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 SQL Server 連線")]
    public string ListConnections() =>
        JsonSerializer.Serialize(
            registry.All.Select(kv => new {
                name = kv.Key,
                host = kv.Value.Host,
                port = kv.Value.Port,
                user = kv.Value.User,
                default_database = kv.Value.Database,
            }), Json
        );

    /// <summary>
    /// 測試指定連線是否正常（回傳伺服器版本）
    /// </summary>
    [McpServerTool, Description("測試指定連線是否正常（回傳伺服器版本）")]
    public string PingConnection(
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            using SqlConnection conn = registry.Open(connection);
            using SqlCommand cmd = new("SELECT @@VERSION", conn) { CommandTimeout = 10 };
            string ver = cmd.ExecuteScalar()?.ToString() ?? "";

            return $"OK: {ver[..Math.Min(120, ver.Length)]}";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 列出指定連線上所有 ONLINE 的資料庫
    /// </summary>
    [McpServerTool, Description("列出指定連線上所有 ONLINE 的資料庫")]
    public string ListDatabases(
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using SqlConnection conn = registry.Open(connection);
            using SqlCommand cmd = new(
                "SELECT name FROM sys.databases WHERE state_desc='ONLINE' ORDER BY name",
                conn
            ) {
                CommandTimeout = QueryTimeout
            };
            using SqlDataReader rdr = cmd.ExecuteReader();
            List<string> names = [];

            while (rdr.Read()) {
                names.Add(rdr.GetString(0));
            }

            return names.Count > 0 ? string.Join("\n", names) : "No databases found.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 列出指定資料庫中的所有使用者資料表
    /// </summary>
    [McpServerTool, Description("列出指定資料庫中的所有使用者資料表")]
    public string ListTables(
        [Description("資料庫名稱，預設 master")] string database = "master",
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using SqlConnection conn = registry.Open(connection, database);
            using SqlCommand cmd = new(@"
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE='BASE TABLE'
                ORDER BY TABLE_SCHEMA, TABLE_NAME",
                conn
            ) {
                CommandTimeout = QueryTimeout
            };
            using SqlDataReader rdr = cmd.ExecuteReader();
            List<string> rows = [];

            while (rdr.Read()) {
                rows.Add($"{rdr[0]}.{rdr[1]}");
            }

            return rows.Count > 0 ? string.Join("\n", rows) : "No tables found.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 描述資料表欄位結構（名稱、型別、可否 NULL）
    /// </summary>
    [McpServerTool, Description("描述資料表欄位結構（名稱、型別、可否 NULL）")]
    public string DescribeTable(
        [Description("資料表名稱")] string table,
        [Description("資料庫名稱，預設 master")] string database = "master",
        [Description("Schema 名稱，預設 dbo")] string schema = "dbo",
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using SqlConnection conn = registry.Open(connection, database);
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

            using SqlDataReader rdr = cmd.ExecuteReader();
            List<string> lines = [];

            while (rdr.Read()) {
                string typeName = rdr.GetString(1);

                if (!rdr.IsDBNull(3)) {
                    typeName += $"({rdr[3]})";
                } else if (!rdr.IsDBNull(4)) {
                    typeName += $"({rdr[4]},{rdr[5]})";
                }

                lines.Add($"{rdr[0]}: {typeName} {(rdr.GetString(2) == "YES" ? "NULL" : "NOT NULL")}");
            }

            return lines.Count > 0 ? string.Join("\n", lines) : "Table not found.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 執行唯讀 SQL 查詢（僅允許 SELECT / WITH）
    /// </summary>
    [McpServerTool, Description("執行唯讀 SQL 查詢（僅允許 SELECT / WITH）")]
    public string Query(
        [Description("SQL 查詢（SELECT 或 WITH 開頭）")] string sql,
        [Description("資料庫名稱")] string database = "master",
        [Description("連線名稱")] string connection = ""
    ) {
        return RunQuery(sql, database, connection);
    }

    /// <summary>
    /// 執行唯讀 SQL 查詢（Base64）。避免傳輸過程清洗字元。
    /// </summary>
    [McpServerTool, Description(
        "執行唯讀 SQL 查詢（Base64）。避免傳輸過程清洗字元。" +
        "當字串常值、JSON 等內容被清洗（例如單引號消失）時，請改用本工具。")]
    public string QueryBase64(
        [Description("Base64 編碼的 SQL（SELECT 或 WITH 開頭）")] string sqlBase64,
        [Description("資料庫名稱")] string database = "master",
        [Description("連線名稱")] string connection = ""
    ) {
        if (!TryDecodeBase64(sqlBase64, out string sql, out string error)) {
            return $"Error: {error}";
        }

        return RunQuery(sql, database, connection);
    }

    private string RunQuery(string sql, string database, string connection) {
        string upper = sql.TrimStart().ToUpperInvariant();

        if (!upper.StartsWith("SELECT") && !upper.StartsWith("WITH")) {
            return "Error: 僅允許 SELECT / WITH 查詢。";
        }

        try {
            SqlGuard.Validate(sql);
            using SqlConnection conn = registry.Open(connection, database);
            using SqlCommand cmd = new(sql, conn) { CommandTimeout = QueryTimeout };
            using SqlDataReader rdr = cmd.ExecuteReader();

            return FormatResultSet(rdr);
        } catch (InvalidOperationException ex) {
            return $"Blocked: {ex.Message}";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool, Description(
        "執行 SQL（INSERT / UPDATE / DELETE / DDL）。" +
        "禁止 DROP TABLE、TRUNCATE；DELETE/UPDATE 必須附帶有意義的 WHERE 條件。")]
    public string Execute(
        [Description("SQL 語句")] string sql,
        [Description("資料庫名稱")] string database = "master",
        [Description("連線名稱")] string connection = ""
    ) {
        return RunExecute(sql, database, connection);
    }

    /// <summary>
    /// 執行 SQL（Base64）。避免傳輸過程清洗字元。
    /// </summary>
    [McpServerTool, Description(
        "執行 SQL（Base64）。避免傳輸過程清洗字元。" +
        "當字串常值、JSON 等內容被清洗（例如單引號消失）時，請改用本工具。" +
        "禁止 DROP TABLE、TRUNCATE；DELETE/UPDATE 必須附帶有意義的 WHERE 條件。")]
    public string ExecuteBase64(
        [Description("Base64 編碼的 SQL")] string sqlBase64,
        [Description("資料庫名稱")] string database = "master",
        [Description("連線名稱")] string connection = ""
    ) {
        if (!TryDecodeBase64(sqlBase64, out string sql, out string error)) {
            return $"Error: {error}";
        }

        return RunExecute(sql, database, connection);
    }

    private string RunExecute(string sql, string database, string connection) {
        try {
            SqlGuard.Validate(sql);
            using SqlConnection conn = registry.Open(connection, database);
            using SqlCommand cmd = new(sql, conn) {
                CommandTimeout = QueryTimeout
            };
            string upper = sql.TrimStart().ToUpperInvariant();

            if (upper.StartsWith("SELECT") || upper.StartsWith("WITH")) {
                using SqlDataReader rdr = cmd.ExecuteReader();
                return FormatResultSet(rdr);
            }

            int affected = cmd.ExecuteNonQuery();

            return affected >= 0 ? $"{affected} row(s) affected." : "Command executed successfully.";
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

    private static string FormatResultSet(SqlDataReader rdr) {
        StringBuilder sb = new();
        int max = MaxRows;
        string[] cols = Enumerable.Range(0, rdr.FieldCount).Select(i => rdr.GetName(i)).ToArray();

        sb.AppendLine(string.Join(" | ", cols));
        sb.AppendLine(string.Join(" | ", cols.Select(_ => "---")));

        int count = 0;

        while (rdr.Read() && count < max) {
            sb.AppendLine(string.Join(" | ",
                Enumerable.Range(0, rdr.FieldCount)
                    .Select(i => rdr.IsDBNull(i) ? "NULL" : rdr[i]?.ToString() ?? "")));
            count++;
        }

        if (count == max) {
            sb.Append($"... (truncated to {max} rows)");
        }

        return sb.ToString().TrimEnd();
    }
}
