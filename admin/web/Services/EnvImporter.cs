using System.Text.RegularExpressions;
using CloudyWing.McpLab.Shared.Configuration;
using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// Where one key of an imported .env file goes.
/// </summary>
/// <param name="Key">Variable name.</param>
/// <param name="Services">Services that receive the key; empty when it is not imported.</param>
/// <param name="Note">Why the key is skipped, or <see langword="null"/> when it is imported.</param>
/// <param name="IsSecret">Whether the value is a credential, so the UI hides it.</param>
public sealed record ImportItem(string Key, IReadOnlyList<string> Services, string? Note, bool IsSecret);

/// <summary>
/// Problems found in a service file after the import is merged into it.
/// </summary>
/// <param name="Service">Service name.</param>
/// <param name="Rejected">Connections that would not load.</param>
/// <param name="Ignored">Keys the service would ignore.</param>
public sealed record ImportServiceCheck(string Service, IReadOnlyList<RejectedConnection> Rejected, IReadOnlyList<IgnoredSetting> Ignored);

/// <summary>
/// Preview of an import.
/// </summary>
public sealed record ImportPreview(
    IReadOnlyList<ImportItem> Items,
    IReadOnlyDictionary<string, string> MergedFiles,
    IReadOnlyList<ImportServiceCheck> Checks
);

/// <summary>
/// Splits a pasted .env file into the per-service config files, the same way <c>scripts/init-config.py</c> does.
/// </summary>
public static partial class EnvImporter {
    /// <summary>
    /// Classifies every key and merges the imported keys into the current service files, without saving.
    /// Throws <see cref="FormatException"/> when the text is not valid .env syntax.
    /// </summary>
    public static ImportPreview Preview(string envText, ConfigStore store) {
        IReadOnlyDictionary<string, string> values = DotEnvParser.Parse(envText);
        List<ImportItem> items = [];
        Dictionary<string, EnvFile> files = new(StringComparer.Ordinal);

        foreach ((string key, string value) in values) {
            if (ComposeKeyRegex().IsMatch(key)) {
                items.Add(new(key, [], "Compose 層設定，請保留在根目錄 .env。", false));
                continue;
            }

            List<string> targets = ServiceCatalog.SharedKeys.Contains(key)
                ? [.. ServiceCatalog.All.Select(service => service.Name)]
                : [.. ServiceCatalog.All.Where(service => service.Owns(key)).Select(service => service.Name)];

            if (targets.Count == 0) {
                items.Add(new(key, [], "無法判斷屬於哪個服務，不會匯入。", false));
                continue;
            }

            if (value.Length == 0) {
                items.Add(new(key, [], "值為空白，不會匯入。", false));
                continue;
            }

            foreach (string service in targets) {
                if (!files.TryGetValue(service, out EnvFile? file)) {
                    file = EnvFile.Parse(store.Read(service));
                    files[service] = file;
                }

                file.Set(key, value);
            }

            items.Add(new(key, targets, null, SensitiveDataSanitizer.IsSensitiveName(key)));
        }

        List<ImportServiceCheck> checks = [];
        HashSet<string> importedKeys = [.. items.Where(item => item.Note is null).Select(item => item.Key)];
        HashSet<string> ignoredKeys = new(StringComparer.Ordinal);

        foreach ((string service, EnvFile file) in files) {
            ConnectionSchema? schema = ServiceCatalog.Find(service)?.Schema;

            if (schema is null) {
                continue;
            }

            ConnectionParseResult parsed = ConnectionSettingsParser.Parse(schema, file.Values);

            // A key the service would ignore is useless in the file, so imported ones are dropped.
            foreach (IgnoredSetting ignored in parsed.Ignored.Where(item => importedKeys.Contains(item.Key))) {
                file.Remove(ignored.Key);
                ignoredKeys.Add(ignored.Key);
            }

            if (parsed.Rejected.Count > 0 || parsed.Ignored.Count > 0) {
                checks.Add(new(service, parsed.Rejected, parsed.Ignored));
            }
        }

        items = [.. items.Select(item => ignoredKeys.Contains(item.Key)
            ? item with { Services = [], Note = "服務會忽略這個變數（原因見上方），不會匯入。" }
            : item)];

        return new(items, files.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()), checks);
    }

    [GeneratedRegex(@"^(COMPOSE_PROJECT_NAME|TZ|PYTHON_TAG|DOTNET_TAG|MCP_[A-Z_]+_PORT|MCP_BIND_ADDRESS|MCP_ADMIN_BIND_ADDRESS|DOCKER_GID|DOCKER_SOCKET_PATH|DOCKER_PROXY_TAG|MCPLAB_RUNTIME_(UID|GID)|DOCUMENT_HOST_MOUNT_(ROOT|SOURCE)|MCP_SMOKE_[A-Z_]+|MCPLAB_ADMIN_PASSWORD)$")]
    private static partial Regex ComposeKeyRegex();
}
