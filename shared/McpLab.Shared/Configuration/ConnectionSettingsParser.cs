using System.Globalization;
using System.Text.RegularExpressions;

namespace CloudyWing.McpLab.Shared.Configuration;

/// <summary>
/// Validated values of one connection, with defaults applied.
/// </summary>
public sealed class ConnectionValues {
    private readonly IReadOnlyDictionary<string, string> values;

    internal ConnectionValues(string alias, string name, IReadOnlyDictionary<string, string> values) {
        Alias = alias;
        Name = name;
        this.values = values;
    }

    /// <summary>Gets the alias part of the variable names, for example <c>PROD</c>.</summary>
    public string Alias { get; }

    /// <summary>Gets the connection name used by tools.</summary>
    public string Name { get; }

    /// <summary>Gets a field value, or an empty string when unset.</summary>
    public string GetString(string field) => values.GetValueOrDefault(field.ToUpperInvariant()) ?? "";

    /// <summary>Gets an integer field validated by the schema.</summary>
    public int GetInt(string field) => int.Parse(GetString(field), CultureInfo.InvariantCulture);

    /// <summary>Gets a boolean field validated by the schema.</summary>
    public bool GetBool(string field) => bool.Parse(GetString(field));

    /// <summary>Gets a comma-separated list field.</summary>
    public IReadOnlyList<string> GetList(string field) =>
        GetString(field).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// A connection that could not be loaded.
/// </summary>
/// <param name="Alias">Alias part of the variable names.</param>
/// <param name="Name">Configured or derived connection name.</param>
/// <param name="Errors">Reasons, each naming the variable to fix.</param>
public sealed record RejectedConnection(string Alias, string Name, IReadOnlyList<string> Errors);

/// <summary>
/// A variable that looks like a connection setting but was not used.
/// </summary>
/// <param name="Key">Variable name.</param>
/// <param name="Reason">Why it was ignored.</param>
public sealed record IgnoredSetting(string Key, string Reason);

/// <summary>
/// Result of parsing connection settings for one service.
/// </summary>
public sealed record ConnectionParseResult(
    IReadOnlyList<ConnectionValues> Connections,
    IReadOnlyList<RejectedConnection> Rejected,
    IReadOnlyList<IgnoredSetting> Ignored
);

/// <summary>
/// Parses <c>{PREFIX}_{ALIAS}_{FIELD}</c> variables against a <see cref="ConnectionSchema"/>.
/// </summary>
public static partial class ConnectionSettingsParser {
    /// <summary>
    /// Groups variables by alias, applies defaults, and validates each connection.
    /// Aliases whose values are all empty (blank templates) are skipped silently.
    /// </summary>
    public static ConnectionParseResult Parse(ConnectionSchema schema, IReadOnlyDictionary<string, string> snapshot) {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(snapshot);

        string keyPrefix = schema.Prefix + "_";
        Dictionary<string, Dictionary<string, string>> buckets = new(StringComparer.Ordinal);
        List<string> aliasOrder = [];
        List<IgnoredSetting> ignored = [];

        foreach ((string key, string rawValue) in snapshot) {
            if (!key.StartsWith(keyPrefix, StringComparison.Ordinal)) {
                continue;
            }

            string rest = key[keyPrefix.Length..];
            int separator = rest.IndexOf('_');

            if (separator <= 0 || separator == rest.Length - 1) {
                ignored.Add(new(key, $"Expected {schema.Prefix}_<ALIAS>_<FIELD>."));
                continue;
            }

            string alias = rest[..separator];
            string fieldName = rest[(separator + 1)..];

            if (!AliasRegex().IsMatch(alias)) {
                ignored.Add(new(key, "ALIAS must use upper-case letters and digits only."));
                continue;
            }

            ConnectionField? field = schema.FindField(fieldName);

            if (field is null) {
                ignored.Add(new(key, DescribeUnknownField(schema, alias, fieldName)));
                continue;
            }

            if (!buckets.TryGetValue(alias, out Dictionary<string, string>? bucket)) {
                bucket = new(StringComparer.Ordinal);
                buckets[alias] = bucket;
                aliasOrder.Add(alias);
            }

            string value = rawValue.Trim();

            if (value.Length > 0 || !bucket.ContainsKey(field.Name)) {
                bucket[field.Name] = value;
            }
        }

        List<ConnectionValues> connections = [];
        List<RejectedConnection> rejected = [];
        Dictionary<string, string> aliasByName = new(StringComparer.OrdinalIgnoreCase);

        foreach (string alias in aliasOrder) {
            Dictionary<string, string> bucket = buckets[alias];

            if (bucket.Values.All(string.IsNullOrEmpty)) {
                continue;
            }

            List<string> errors = [];
            Dictionary<string, string> values = new(StringComparer.Ordinal);

            foreach (ConnectionField field in schema.Fields) {
                string value = bucket.GetValueOrDefault(field.Name) ?? "";

                if (value.Length == 0) {
                    value = field.DefaultValue ?? "";
                }

                string variable = schema.VariableName(alias, field.Name);

                if (value.Length == 0) {
                    if (field.Required) {
                        errors.Add($"{variable} is required.");
                    }

                    values[field.Name] = "";
                    continue;
                }

                if (ValidateValue(field.Kind, value) is string error) {
                    errors.Add($"{variable} {error}");
                } else if (field.Choices is { } choices) {
                    string? match = choices.FirstOrDefault(choice => string.Equals(choice, value, StringComparison.OrdinalIgnoreCase));

                    if (match is null) {
                        errors.Add($"{variable} must be one of: {string.Join(", ", choices)}.");
                    } else {
                        value = match;
                    }
                }

                values[field.Name] = value;
            }

            foreach (IReadOnlyList<string> group in schema.RequireAnyOf ?? []) {
                if (group.All(field => string.IsNullOrEmpty(values.GetValueOrDefault(field)))) {
                    errors.Add(string.Join(" or ", group.Select(field => schema.VariableName(alias, field))) + " is required.");
                }
            }

            string name = values.GetValueOrDefault(ConnectionSchema.NameField) is { Length: > 0 } configured
                ? configured
                : alias.ToLowerInvariant();

            if (aliasByName.TryGetValue(name, out string? existingAlias)) {
                errors.Add($"NAME '{name}' is already used by {schema.VariableName(existingAlias, ConnectionSchema.NameField)}.");
            }

            if (errors.Count > 0) {
                // A rejected connection still owns its name, so another alias cannot silently take it over.
                aliasByName.TryAdd(name, alias);
                rejected.Add(new(alias, name, errors));
                continue;
            }

            values[ConnectionSchema.NameField] = name;
            aliasByName[name] = alias;
            connections.Add(new ConnectionValues(alias, name, values));
        }

        return new ConnectionParseResult(connections, rejected, ignored);
    }

    private static string? ValidateValue(ConnectionFieldKind kind, string value) {
        return kind switch {
            ConnectionFieldKind.Port when !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                || port is < 1 or > 65535 => "must be a port number between 1 and 65535.",
            ConnectionFieldKind.NonNegativeInt when !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                => "must be an integer of 0 or greater.",
            ConnectionFieldKind.PositiveInt when !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int positive)
                || positive < 1 => "must be an integer of 1 or greater.",
            ConnectionFieldKind.Bool when !bool.TryParse(value, out _) => "must be true or false.",
            ConnectionFieldKind.Url when !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                => "must be an absolute http or https URL.",
            _ => null,
        };
    }

    private static string DescribeUnknownField(ConnectionSchema schema, string alias, string fieldName) {
        ConnectionField? suffixMatch = schema.Fields.FirstOrDefault(
            field => fieldName.EndsWith("_" + field.Name, StringComparison.Ordinal)
        );

        if (suffixMatch is not null) {
            string fullAlias = $"{alias}_{fieldName[..^(suffixMatch.Name.Length + 1)]}";
            return $"ALIAS cannot contain underscores: {fullAlias}. Use {fullAlias.Replace("_", "", StringComparison.Ordinal)} instead.";
        }

        return $"Unknown field {fieldName}. Known fields: {string.Join(", ", schema.Fields.Select(field => field.Name))}.";
    }

    [GeneratedRegex("^[A-Z0-9]+$")]
    private static partial Regex AliasRegex();
}
