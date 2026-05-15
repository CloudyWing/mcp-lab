namespace CloudyWing.McpLab.SqlServer;

/// <summary>
/// Validates SQL statements before execution.
/// Blocks: DROP TABLE/DATABASE/SCHEMA, TRUNCATE.
/// Requires explicit WHERE for DELETE/UPDATE; rejects trivial always-true conditions.
/// </summary>
public static partial class SqlGuard {
    private static readonly (Regex Pattern, string Label)[] Forbidden = [
        (DropTableRegex(), "DROP TABLE"),
        (DropDatabaseRegex(), "DROP DATABASE"),
        (DropSchemaRegex(), "DROP SCHEMA"),
        (TruncateRegex(), "TRUNCATE"),
    ];

    // Match typical trivial / always-true WHERE conditions
    private static readonly Regex[] TrivialWhere = [
        WhereNumberEqualsRegex(), // WHERE 1=1
        WhereStringEqualsRegex(), // WHERE 'a'='a'
        WhereUnicodeStringEqualsRegex(), // WHERE N'x'=N'x'
        WhereDoubleQuotedEqualsRegex(), // WHERE "a"="a"
        WhereTrueRegex(), // WHERE TRUE
        OrNumberEqualsRegex(), // OR 1=1
        OrStringEqualsRegex(), // OR 'a'='a'
        OrTrueRegex(), // OR TRUE
    ];

    /// <summary>
    /// Strip /* */ block comments and -- line comments before analysis.
    /// </summary>
    private static string StripComments(string sql) {
        string s = BlockCommentRegex().Replace(sql, " ");

        return LineCommentRegex().Replace(s, " ");
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the SQL violates policy.
    /// </summary>
    public static void Validate(string sql) {
        string clean = StripComments(sql);
        string masked = MaskLiterals(clean);

        foreach ((Regex pattern, string label) in Forbidden) {
            if (pattern.IsMatch(clean)) {
                throw new InvalidOperationException($"Forbidden operation: {label} is not allowed.");
            }
        }

        bool isDelete = DeleteKeywordRegex().IsMatch(masked);
        bool isUpdate = UpdateKeywordRegex().IsMatch(masked);

        if (!isDelete && !isUpdate) {
            return;
        }

        if (!WhereKeywordRegex().IsMatch(masked)) {
            string op = isDelete ? "DELETE" : "UPDATE";
            throw new InvalidOperationException($"{op} requires a WHERE clause.");
        }

        foreach (Regex rx in TrivialWhere) {
            if (rx.IsMatch(masked)) {
                throw new InvalidOperationException("Always-true WHERE conditions are not allowed in DELETE/UPDATE.");
            }
        }
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a read-only query contains DML, DDL, or command keywords.
    /// </summary>
    public static void ValidateReadOnlyQuery(string sql) {
        string clean = StripComments(sql);
        string masked = MaskLiterals(clean);
        string upper = masked.TrimStart().ToUpperInvariant();

        if (!upper.StartsWith("SELECT") && !upper.StartsWith("WITH")) {
            throw new InvalidOperationException("Only SELECT / WITH read queries are allowed.");
        }

        if (ReadOnlyForbiddenKeywordRegex().IsMatch(masked)) {
            throw new InvalidOperationException("Only SELECT / WITH read queries are allowed.");
        }
    }

    private static string MaskLiterals(string sql) {
        string masked = SingleQuotedLiteralRegex().Replace(sql, "''");
        masked = DoubleQuotedLiteralRegex().Replace(masked, "\"\"");

        return BracketedIdentifierRegex().Replace(masked, "[]");
    }

    [GeneratedRegex(@"\bDROP\s+TABLE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropTableRegex();

    [GeneratedRegex(@"\bDROP\s+DATABASE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropDatabaseRegex();

    [GeneratedRegex(@"\bDROP\s+SCHEMA\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropSchemaRegex();

    [GeneratedRegex(@"\bTRUNCATE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TruncateRegex();

    [GeneratedRegex(@"\bWHERE\b\s+(\d+)\s*=\s*\1", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereNumberEqualsRegex();

    [GeneratedRegex(@"\bWHERE\b\s+'([^']*)'\s*=\s*'\1'", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereStringEqualsRegex();

    [GeneratedRegex(@"\bWHERE\b\s+N'([^']*)'\s*=\s*N'\1'", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereUnicodeStringEqualsRegex();

    [GeneratedRegex(@"\bWHERE\b\s+""([^""]*)""\s*=\s*""\1""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereDoubleQuotedEqualsRegex();

    [GeneratedRegex(@"\bWHERE\b\s+TRUE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereTrueRegex();

    [GeneratedRegex(@"\bOR\b\s+(\d+)\s*=\s*\1", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OrNumberEqualsRegex();

    [GeneratedRegex(@"\bOR\b\s+'([^']*)'\s*=\s*'\1'", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OrStringEqualsRegex();

    [GeneratedRegex(@"\bOR\b\s+TRUE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex OrTrueRegex();

    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"--[^\r\n]*")]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex(@"\bWHERE\b", RegexOptions.IgnoreCase)]
    private static partial Regex WhereKeywordRegex();

    [GeneratedRegex(@"\bDELETE\b", RegexOptions.IgnoreCase)]
    private static partial Regex DeleteKeywordRegex();

    [GeneratedRegex(@"\bUPDATE\b", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateKeywordRegex();

    [GeneratedRegex(@"\b(?:INSERT|UPDATE|DELETE|MERGE|EXEC|EXECUTE|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|DENY|BACKUP|RESTORE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyForbiddenKeywordRegex();

    [GeneratedRegex(@"N?'(?:''|[^'])*'", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SingleQuotedLiteralRegex();

    [GeneratedRegex(@"""(?:""""|[^""])*""", RegexOptions.Singleline)]
    private static partial Regex DoubleQuotedLiteralRegex();

    [GeneratedRegex(@"\[[^\]]+\]", RegexOptions.Singleline)]
    private static partial Regex BracketedIdentifierRegex();
}
