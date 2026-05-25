using System.Text;
using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Shared.DotEnv;

/// <summary>
/// Maintains the current .env key-value snapshot.
/// </summary>
public sealed partial class DotEnvSnapshotProvider : IDotEnvSnapshotProvider {
    private readonly DotEnvFileSource source;
    private readonly ILogger<DotEnvSnapshotProvider> logger;
    private readonly IReadOnlyDictionary<string, string> baseline;
    private IReadOnlyDictionary<string, string> current;

    /// <summary>
    /// Initializes a new instance of <see cref="DotEnvSnapshotProvider"/>.
    /// </summary>
    /// <param name="source">The .env file source.</param>
    /// <param name="logger">The logger.</param>
    public DotEnvSnapshotProvider(DotEnvFileSource source, ILogger<DotEnvSnapshotProvider> logger)
        : this(source, logger, LoadEnvironmentSnapshot()) {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DotEnvSnapshotProvider"/>.
    /// </summary>
    /// <param name="source">The .env file source.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="baseline">The baseline values used when the .env file omits a key.</param>
    public DotEnvSnapshotProvider(
        DotEnvFileSource source,
        ILogger<DotEnvSnapshotProvider> logger,
        IReadOnlyDictionary<string, string> baseline
    ) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(baseline);

        this.source = source;
        this.logger = logger;
        this.baseline = new Dictionary<string, string>(baseline, StringComparer.Ordinal);
        current = this.baseline;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Current => Volatile.Read(ref current);

    /// <inheritdoc/>
    public event EventHandler<DotEnvReloadedEventArgs>? Reloaded;

    /// <summary>
    /// Reloads the .env file if it can be parsed successfully.
    /// </summary>
    /// <param name="error">The reload error when parsing failed.</param>
    /// <returns><see langword="true" /> when reload succeeds; otherwise <see langword="false" />.</returns>
    public bool TryReload(out string? error) {
        IReadOnlyDictionary<string, string> parsed;

        try {
            parsed = DotEnvParser.Parse(source.ReadAllText());
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException or FormatException) {
            error = ex.Message;
            return false;
        }

        error = null;
        IReadOnlyDictionary<string, string> next = MergeWithBaseline(parsed);
        IReadOnlyDictionary<string, string> previous = Current;

        if (AreEqual(previous, next)) {
            return true;
        }

        DotEnvReloadedEventArgs args = BuildReloadedEventArgs(previous, next);
        Volatile.Write(ref current, next);
        LogReloaded(
            logger,
            FormatKeys(args.AddedKeys),
            FormatKeys(args.RemovedKeys),
            FormatKeys(args.ChangedKeys)
        );
        Reloaded?.Invoke(this, args);

        return true;
    }

    private static IReadOnlyDictionary<string, string> LoadEnvironmentSnapshot() {
        Dictionary<string, string> result = new(StringComparer.Ordinal);

        foreach (string key in Environment.GetEnvironmentVariables().Keys.OfType<string>()) {
            result[key] = Environment.GetEnvironmentVariable(key) ?? "";
        }

        return result;
    }

    private IReadOnlyDictionary<string, string> MergeWithBaseline(IReadOnlyDictionary<string, string> values) {
        Dictionary<string, string> result = new(baseline, StringComparer.Ordinal);

        foreach ((string key, string value) in values) {
            result[key] = value;
        }

        return result;
    }

    private static bool AreEqual(
        IReadOnlyDictionary<string, string> previous,
        IReadOnlyDictionary<string, string> next
    ) {
        if (previous.Count != next.Count) {
            return false;
        }

        foreach ((string key, string value) in previous) {
            if (!next.TryGetValue(key, out string? nextValue) || !string.Equals(value, nextValue, StringComparison.Ordinal)) {
                return false;
            }
        }

        return true;
    }

    private static DotEnvReloadedEventArgs BuildReloadedEventArgs(
        IReadOnlyDictionary<string, string> previous,
        IReadOnlyDictionary<string, string> next
    ) {
        string[] addedKeys = next.Keys
            .Except(previous.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] removedKeys = previous.Keys
            .Except(next.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] changedKeys = next.Keys
            .Intersect(previous.Keys, StringComparer.Ordinal)
            .Where(key => !string.Equals(next[key], previous[key], StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new DotEnvReloadedEventArgs(previous, next, addedKeys, removedKeys, changedKeys);
    }

    private static string FormatKeys(IEnumerable<string> keys) => $"[{string.Join(", ", keys)}]";

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = ".env reloaded; AddedKeys: {AddedKeys}; RemovedKeys: {RemovedKeys}; ChangedKeys: {ChangedKeys}"
    )]
    private static partial void LogReloaded(
        ILogger logger,
        string addedKeys,
        string removedKeys,
        string changedKeys
    );
}
