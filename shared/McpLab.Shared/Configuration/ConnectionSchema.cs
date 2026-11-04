namespace CloudyWing.McpLab.Shared.Configuration;

/// <summary>
/// Value kinds a connection field can hold.
/// </summary>
public enum ConnectionFieldKind {
    /// <summary>Free text.</summary>
    Text,

    /// <summary>Credential; never returned by status or listing endpoints.</summary>
    Secret,

    /// <summary>TCP port between 1 and 65535.</summary>
    Port,

    /// <summary>Integer zero or greater.</summary>
    NonNegativeInt,

    /// <summary>Integer one or greater.</summary>
    PositiveInt,

    /// <summary>true or false.</summary>
    Bool,

    /// <summary>Absolute http or https URL.</summary>
    Url,

    /// <summary>Comma-separated list.</summary>
    List,
}

/// <summary>
/// One field of a connection, such as HOST or PASSWORD.
/// </summary>
/// <param name="Name">Field name as written after the alias, in upper case (for example <c>SSL_SKIP_VERIFY</c>).</param>
/// <param name="Kind">Value kind used for validation.</param>
/// <param name="Description">Short description shown to people editing the setting.</param>
/// <param name="Required">Whether the field must have a value.</param>
/// <param name="DefaultValue">Value used when the field is empty.</param>
/// <param name="Aliases">Alternative field names accepted for compatibility.</param>
/// <param name="Choices">Allowed values, compared case-insensitively; <see langword="null"/> allows any value.</param>
public sealed record ConnectionField(
    string Name,
    ConnectionFieldKind Kind,
    string Description,
    bool Required = false,
    string? DefaultValue = null,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<string>? Choices = null
) {
    /// <summary>
    /// Gets every accepted field name, starting with <see cref="Name"/>.
    /// </summary>
    public IEnumerable<string> AllNames => Aliases is null ? [Name] : [Name, .. Aliases];
}

/// <summary>
/// Describes the connection settings of one MCP service.
/// </summary>
/// <param name="Service">Compose service name, for example <c>mcp-sql-server</c>.</param>
/// <param name="Prefix">Variable prefix without the trailing underscore, for example <c>MSSQL_CONN</c>.</param>
/// <param name="Fields">Accepted fields.</param>
/// <param name="RequireAnyOf">Groups of fields where at least one must have a value.</param>
public sealed record ConnectionSchema(
    string Service,
    string Prefix,
    IReadOnlyList<ConnectionField> Fields,
    IReadOnlyList<IReadOnlyList<string>>? RequireAnyOf = null
) {
    /// <summary>
    /// Name of the field holding the connection name used by tools.
    /// </summary>
    public const string NameField = "NAME";

    /// <summary>
    /// Name of the optional field that labels a connection's environment, for display only.
    /// </summary>
    public const string EnvironmentField = "ENVIRONMENT";

    /// <summary>
    /// Finds a field by its name or alias, ignoring case.
    /// </summary>
    public ConnectionField? FindField(string name) =>
        Fields.FirstOrDefault(field => field.AllNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Builds the full variable name for an alias and field, for example <c>MSSQL_CONN_PROD_HOST</c>.
    /// </summary>
    public string VariableName(string alias, string field) => $"{Prefix}_{alias}_{field}";
}
