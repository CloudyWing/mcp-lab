using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// One recorded change. It never holds setting values.
/// </summary>
/// <param name="Time">When the change was made.</param>
/// <param name="Service">Service whose config changed.</param>
/// <param name="Action">What was changed, for example 新增連線 PROD.</param>
/// <param name="Outcome">Result of saving.</param>
/// <param name="Message">Result message.</param>
public sealed record AuditEntry(
    [property: JsonPropertyName("time")] DateTimeOffset Time,
    [property: JsonPropertyName("service")] string Service,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("outcome")] ApplyOutcome Outcome,
    [property: JsonPropertyName("message")] string Message
);

/// <summary>
/// Append-only change log stored in <c>config/.admin/audit.jsonl</c>.
/// </summary>
public sealed class AuditLog {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="AuditLog"/>.
    /// </summary>
    public AuditLog(ConfigStore store) {
        path = Path.Combine(store.AdminDirectory, "audit.jsonl");
    }

    /// <summary>
    /// Appends an entry.
    /// </summary>
    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(entry, JsonOptions) + "\n", cancellationToken).ConfigureAwait(false);
        } finally {
            gate.Release();
        }
    }

    /// <summary>
    /// Reads the newest entries, optionally for one service.
    /// </summary>
    public IReadOnlyList<AuditEntry> ReadLatest(int count, string? service = null) {
        if (!File.Exists(path)) {
            return [];
        }

        List<AuditEntry> entries = [];

        foreach (string line in File.ReadLines(path)) {
            try {
                if (JsonSerializer.Deserialize<AuditEntry>(line, JsonOptions) is AuditEntry entry
                    && (service is null || entry.Service == service)) {
                    entries.Add(entry);
                }
            } catch (JsonException) {
                // Skip a damaged line rather than hiding the whole history.
            }
        }

        return [.. entries.AsEnumerable().Reverse().Take(count)];
    }
}
