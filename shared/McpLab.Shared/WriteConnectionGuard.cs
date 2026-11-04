namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Requires write tools to name their target connection instead of falling back to the first configured one.
/// </summary>
public static class WriteConnectionGuard {
    /// <summary>
    /// Parameter description for the connection argument of write tools.
    /// </summary>
    public const string ParameterDescription = "連線名稱（寫入操作必填，請先呼叫 list_connections 確認）";

    /// <summary>
    /// Returns a blocked tool response when the connection name is missing; otherwise <see langword="null"/>.
    /// </summary>
    public static string? CheckMissing(string? connection) {
        return string.IsNullOrWhiteSpace(connection)
            ? ToolResponse.Blocked(
                "Write operations require an explicit connection name. Call list_connections() and pass connection=\"<name>\".")
            : null;
    }
}
