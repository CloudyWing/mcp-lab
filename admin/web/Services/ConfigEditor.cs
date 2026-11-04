using System.Text.RegularExpressions;
using CloudyWing.McpLab.Shared.Configuration;

namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// A connection as shown in the admin. Secret values are never included; only whether they are set.
/// </summary>
/// <param name="Alias">Alias part of the variable names.</param>
/// <param name="Values">Non-secret field values as written in the file (without defaults).</param>
/// <param name="SecretsSet">Secret fields that have a value.</param>
/// <param name="State"><c>loaded</c>, <c>rejected</c>, or <c>empty</c> for a blank template, as judged from the file.</param>
/// <param name="Name">Connection name used by tools.</param>
/// <param name="Errors">Reasons the connection is rejected.</param>
public sealed record ConnectionEntry(
    string Alias,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlySet<string> SecretsSet,
    string State,
    string Name,
    IReadOnlyList<string> Errors
);

/// <summary>
/// Changes requested for one connection in the editor.
/// </summary>
public sealed class ConnectionDraft {
    /// <summary>Gets or sets the alias before editing, or <see langword="null"/> for a new connection.</summary>
    public string? OriginalAlias { get; set; }

    /// <summary>Gets or sets the alias.</summary>
    public string Alias { get; set; } = "";

    /// <summary>Gets non-secret field values; empty means unset (the default applies).</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets new secret values; a secret not listed keeps its current value.</summary>
    public Dictionary<string, string> NewSecrets { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets secrets to remove.</summary>
    public HashSet<string> ClearedSecrets { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Applies admin edits to an <see cref="EnvFile"/> using a service's connection schema.
/// </summary>
public static partial class ConfigEditor {
    /// <summary>
    /// Lists connections in a file, including blank templates, in file order.
    /// </summary>
    public static IReadOnlyList<ConnectionEntry> ListConnections(ConnectionSchema schema, EnvFile file) {
        IReadOnlyDictionary<string, string> values = file.Values;
        ConnectionParseResult parsed = ConnectionSettingsParser.Parse(schema, values);
        List<ConnectionEntry> entries = [];

        foreach (string alias in AliasesOf(schema, values.Keys)) {
            Dictionary<string, string> fields = new(StringComparer.Ordinal);
            HashSet<string> secrets = new(StringComparer.Ordinal);

            foreach (ConnectionField field in schema.Fields) {
                string value = field.AllNames
                    .Select(name => values.GetValueOrDefault(schema.VariableName(alias, name)) ?? "")
                    .FirstOrDefault(candidate => candidate.Length > 0) ?? "";

                if (field.Kind == ConnectionFieldKind.Secret) {
                    if (value.Length > 0) {
                        secrets.Add(field.Name);
                    }
                } else {
                    fields[field.Name] = value;
                }
            }

            ConnectionValues? loaded = parsed.Connections.FirstOrDefault(item => item.Alias == alias);
            RejectedConnection? rejected = parsed.Rejected.FirstOrDefault(item => item.Alias == alias);
            string name = loaded?.Name ?? rejected?.Name ?? DefaultName(fields, alias);
            string state = loaded is not null ? "loaded" : rejected is not null ? "rejected" : "empty";
            entries.Add(new ConnectionEntry(alias, fields, secrets, state, name, rejected?.Errors ?? []));
        }

        return entries;
    }

    /// <summary>
    /// Validates a draft without changing any file, returning problems to show next to the form.
    /// </summary>
    public static IReadOnlyList<string> Validate(ConnectionSchema schema, EnvFile file, ConnectionDraft draft) {
        List<string> errors = [];

        if (!AliasRegex().IsMatch(draft.Alias)) {
            errors.Add("Alias 只能使用大寫英文字母與數字，例如 PROD、STAGE2。");
            return errors;
        }

        IReadOnlyList<string> existing = AliasesOf(schema, file.Values.Keys);
        if (draft.Alias != draft.OriginalAlias && existing.Contains(draft.Alias)) {
            errors.Add($"Alias {draft.Alias} 已經存在。");
            return errors;
        }

        EnvFile candidate = EnvFile.Parse(file.ToString());
        Apply(schema, candidate, draft);
        ConnectionParseResult parsed = ConnectionSettingsParser.Parse(schema, candidate.Values);
        RejectedConnection? rejected = parsed.Rejected.FirstOrDefault(item => item.Alias == draft.Alias);

        if (rejected is not null) {
            errors.AddRange(rejected.Errors);
        } else if (parsed.Connections.All(item => item.Alias != draft.Alias)) {
            errors.Add("請至少填寫必填欄位。");
        }

        return errors;
    }

    /// <summary>
    /// Writes a draft into the file. Empty values are removed so their defaults apply.
    /// Renaming an alias moves all of its keys, keeping secrets that were not replaced.
    /// </summary>
    public static void Apply(ConnectionSchema schema, EnvFile file, ConnectionDraft draft) {
        IReadOnlyDictionary<string, string> current = file.Values;
        string sourceAlias = draft.OriginalAlias ?? draft.Alias;

        foreach (ConnectionField field in schema.Fields) {
            string target = schema.VariableName(draft.Alias, field.Name);
            string value;

            if (field.Kind == ConnectionFieldKind.Secret) {
                value = draft.ClearedSecrets.Contains(field.Name)
                    ? ""
                    : draft.NewSecrets.TryGetValue(field.Name, out string? replaced) && replaced.Length > 0
                        ? replaced
                        : field.AllNames
                            .Select(name => current.GetValueOrDefault(schema.VariableName(sourceAlias, name)) ?? "")
                            .FirstOrDefault(candidate => candidate.Length > 0) ?? "";
            } else {
                value = (draft.Values.GetValueOrDefault(field.Name) ?? "").Trim();
            }

            foreach (string alias in field.Aliases ?? []) {
                file.Remove(schema.VariableName(draft.Alias, alias));
            }

            if (value.Length == 0) {
                file.Remove(target);
            } else {
                file.Set(target, value);
            }
        }

        if (draft.OriginalAlias is not null && draft.OriginalAlias != draft.Alias) {
            file.RemovePrefix($"{schema.Prefix}_{draft.OriginalAlias}_");
        }
    }

    /// <summary>
    /// Creates a draft that copies an existing connection, including its secrets, under a new alias.
    /// </summary>
    public static ConnectionDraft CopyDraft(ConnectionSchema schema, EnvFile file, string alias, string newAlias) {
        ConnectionEntry source = ListConnections(schema, file).Single(entry => entry.Alias == alias);
        ConnectionDraft draft = new() { Alias = newAlias };

        foreach ((string field, string value) in source.Values) {
            draft.Values[field] = field == ConnectionSchema.NameField ? "" : value;
        }

        foreach (ConnectionField field in schema.Fields.Where(field => field.Kind == ConnectionFieldKind.Secret)) {
            string value = file.Values.GetValueOrDefault(schema.VariableName(alias, field.Name)) ?? "";

            if (value.Length > 0) {
                draft.NewSecrets[field.Name] = value;
            }
        }

        return draft;
    }

    /// <summary>
    /// Removes every key of a connection.
    /// </summary>
    public static void Delete(ConnectionSchema schema, EnvFile file, string alias) {
        file.RemovePrefix($"{schema.Prefix}_{alias}_");
    }

    /// <summary>
    /// Sets or clears a service-level setting. Older fallback names are removed so the new value is the one read.
    /// </summary>
    public static void SetSetting(EnvFile file, string name, IEnumerable<string> fallbacks, string? value) {
        foreach (string fallback in fallbacks) {
            file.Remove(fallback);
        }

        if (string.IsNullOrWhiteSpace(value)) {
            file.Remove(name);
        } else {
            file.Set(name, value.Trim());
        }
    }

    /// <summary>
    /// Suggests an unused alias based on <paramref name="baseAlias"/>, for example PROD2.
    /// </summary>
    public static string SuggestAlias(ConnectionSchema schema, EnvFile file, string baseAlias) {
        IReadOnlyList<string> existing = AliasesOf(schema, file.Values.Keys);

        for (int i = 2; ; i++) {
            string candidate = $"{baseAlias}{i}";

            if (!existing.Contains(candidate)) {
                return candidate;
            }
        }
    }

    private static IReadOnlyList<string> AliasesOf(ConnectionSchema schema, IEnumerable<string> keys) {
        string prefix = schema.Prefix + "_";
        List<string> aliases = [];

        foreach (string key in keys) {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) {
                continue;
            }

            string rest = key[prefix.Length..];
            int separator = rest.IndexOf('_');

            if (separator <= 0) {
                continue;
            }

            string alias = rest[..separator];

            if (AliasRegex().IsMatch(alias) && schema.FindField(rest[(separator + 1)..]) is not null && !aliases.Contains(alias)) {
                aliases.Add(alias);
            }
        }

        return aliases;
    }

    private static string DefaultName(IReadOnlyDictionary<string, string> fields, string alias) =>
        fields.GetValueOrDefault(ConnectionSchema.NameField) is { Length: > 0 } name ? name : alias.ToLowerInvariant();

    [GeneratedRegex("^[A-Z0-9]+$")]
    private static partial Regex AliasRegex();
}
