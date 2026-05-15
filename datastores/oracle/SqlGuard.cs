namespace CloudyWing.McpLab.Oracle;

/// <summary>
/// Validates Oracle SQL / PL/SQL before execution.
/// Mirrors the same policy as SQL Server guard.
/// </summary>
public static partial class SqlGuard {
    private static readonly (Regex Pattern, string Label)[] Forbidden = [
        (DropTableRegex(), "DROP TABLE"),
        (DropUserRegex(), "DROP USER"),
        (DropTablespaceRegex(), "DROP TABLESPACE"),
        (TruncateRegex(), "TRUNCATE"),
    ];

    private static readonly Regex[] TrivialWhere = [
        WhereNumberEqualsRegex(),
        WhereStringEqualsRegex(),
        WhereOneEqualsOneRegex(),
        WhereTrueRegex(),
        OrNumberEqualsRegex(),
        OrStringEqualsRegex(),
        OrTrueRegex(),
    ];

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
                throw new InvalidOperationException(
                    "Always-true WHERE conditions are not allowed in DELETE/UPDATE.");
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

        return DoubleQuotedLiteralRegex().Replace(masked, "\"\"");
    }

    [GeneratedRegex(@"\bDROP\s+TABLE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropTableRegex();

    [GeneratedRegex(@"\bDROP\s+USER\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropUserRegex();

    [GeneratedRegex(@"\bDROP\s+TABLESPACE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropTablespaceRegex();

    [GeneratedRegex(@"\bTRUNCATE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TruncateRegex();

    [GeneratedRegex(@"\bWHERE\b\s+(\d+)\s*=\s*\1", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereNumberEqualsRegex();

    [GeneratedRegex(@"\bWHERE\b\s+'([^']*)'\s*=\s*'\1'", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereStringEqualsRegex();

    [GeneratedRegex(@"\bWHERE\b\s+1\s*=\s*1", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereOneEqualsOneRegex();

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

    [GeneratedRegex(@"\b(?:INSERT|UPDATE|DELETE|MERGE|CALL|BEGIN|DECLARE|CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|FLASHBACK)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyForbiddenKeywordRegex();

    [GeneratedRegex(@"N?'(?:''|[^'])*'", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SingleQuotedLiteralRegex();

    [GeneratedRegex(@"""(?:""""|[^""])*""", RegexOptions.Singleline)]
    private static partial Regex DoubleQuotedLiteralRegex();
}
