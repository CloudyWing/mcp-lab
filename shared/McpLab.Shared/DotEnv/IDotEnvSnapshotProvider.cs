namespace CloudyWing.McpLab.Shared.DotEnv;

/// <summary>
/// Exposes the current snapshot of a .env file and notifies listeners on change.
/// </summary>
public interface IDotEnvSnapshotProvider {
    /// <summary>
    /// Gets the current key-value snapshot.
    /// </summary>
    IReadOnlyDictionary<string, string> Current { get; }

    /// <summary>
    /// Occurs after a successful reload when the snapshot value differs from the previous one.
    /// </summary>
    event EventHandler<DotEnvReloadedEventArgs>? Reloaded;
}
