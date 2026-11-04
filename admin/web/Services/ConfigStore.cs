using System.Collections.Concurrent;
using System.Globalization;
using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// A saved previous version of a service config file.
/// </summary>
/// <param name="Id">File name of the backup, used to restore it.</param>
/// <param name="SavedAt">When the version was replaced.</param>
public sealed record ConfigRevision(string Id, DateTimeOffset SavedAt);

/// <summary>
/// Reads and writes <c>config/&lt;service&gt;/service.env</c>. Writes are atomic (temporary file and rename),
/// keep the file private (0600), and back up the previous content under <c>config/.admin/history</c>,
/// which no MCP service mounts.
/// </summary>
public sealed class ConfigStore {
    /// <summary>
    /// File name of each service's config file.
    /// </summary>
    public const string FileName = "service.env";

    private const int MaxRevisions = 20;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of <see cref="ConfigStore"/>.
    /// </summary>
    /// <param name="root">The config root directory.</param>
    public ConfigStore(string root) {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>
    /// Gets the config root directory.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets the directory that holds admin-only data such as backups and data protection keys.
    /// </summary>
    public string AdminDirectory => Path.Combine(Root, ".admin");

    /// <summary>
    /// Gets the config file path of a service.
    /// </summary>
    public string PathOf(string service) => Path.Combine(ServiceDirectory(service), FileName);

    /// <summary>
    /// Reads a service's config file, or an empty string when it does not exist.
    /// </summary>
    public string Read(string service) {
        string path = PathOf(service);

        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    /// <summary>
    /// Replaces a service's config file after checking its syntax, backing up the previous content.
    /// Throws <see cref="FormatException"/> when the new content is not valid .env syntax.
    /// </summary>
    public async Task WriteAsync(string service, string content, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(content);
        DotEnvParser.Parse(content);

        SemaphoreSlim gate = locks.GetOrAdd(service, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            string path = PathOf(service);
            Directory.CreateDirectory(ServiceDirectory(service));

            if (File.Exists(path)) {
                Backup(service, path);
            }

            string temp = Path.Combine(ServiceDirectory(service), $".{FileName}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temp, content, cancellationToken).ConfigureAwait(false);
            MakePrivate(temp);
            File.Move(temp, path, overwrite: true);
        } finally {
            gate.Release();
        }
    }

    /// <summary>
    /// Lists backups of a service, newest first.
    /// </summary>
    public IReadOnlyList<ConfigRevision> ListRevisions(string service) {
        string directory = HistoryDirectory(service);

        if (!Directory.Exists(directory)) {
            return [];
        }

        return Directory.GetFiles(directory, "*.env")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .Reverse()
            .Select(id => new ConfigRevision(id, ParseRevisionTime(id)))
            .ToList();
    }

    /// <summary>
    /// Reads a backup's content.
    /// </summary>
    public string ReadRevision(string service, string id) {
        if (id.Contains('/') || id.Contains('\\') || id.Contains("..", StringComparison.Ordinal)) {
            throw new ArgumentException("Invalid revision id.", nameof(id));
        }

        return File.ReadAllText(Path.Combine(HistoryDirectory(service), id));
    }

    private void Backup(string service, string path) {
        string directory = HistoryDirectory(service);
        Directory.CreateDirectory(directory);
        string id = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture) + ".env";
        string target = Path.Combine(directory, id);
        File.Copy(path, target, overwrite: true);
        MakePrivate(target);

        foreach (string old in Directory.GetFiles(directory, "*.env").Order(StringComparer.Ordinal).SkipLast(MaxRevisions)) {
            File.Delete(old);
        }
    }

    private string ServiceDirectory(string service) {
        if (ServiceCatalog.Find(service) is null) {
            throw new ArgumentException($"Unknown service '{service}'.", nameof(service));
        }

        return Path.Combine(Root, service);
    }

    private string HistoryDirectory(string service) => Path.Combine(AdminDirectory, "history", ServiceDirectory(service)[(Root.Length + 1)..]);

    private static DateTimeOffset ParseRevisionTime(string id) =>
        DateTimeOffset.TryParseExact(
            id[..^4],
            "yyyyMMdd'T'HHmmssfffffff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset value
        ) ? value : DateTimeOffset.MinValue;

    private static void MakePrivate(string path) {
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
