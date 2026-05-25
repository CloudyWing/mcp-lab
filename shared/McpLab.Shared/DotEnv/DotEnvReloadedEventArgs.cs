namespace CloudyWing.McpLab.Shared.DotEnv;

/// <summary>
/// Describes the outcome of a .env reload.
/// </summary>
/// <param name="Previous">The previous key-value snapshot.</param>
/// <param name="Current">The current key-value snapshot.</param>
/// <param name="AddedKeys">The keys added by the reload.</param>
/// <param name="RemovedKeys">The keys removed by the reload.</param>
/// <param name="ChangedKeys">The keys whose values changed during the reload.</param>
public sealed record DotEnvReloadedEventArgs(
    IReadOnlyDictionary<string, string> Previous,
    IReadOnlyDictionary<string, string> Current,
    IReadOnlyList<string> AddedKeys,
    IReadOnlyList<string> RemovedKeys,
    IReadOnlyList<string> ChangedKeys
);
