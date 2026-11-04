namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// Outcome of saving a config file.
/// </summary>
public enum ApplyOutcome {
    /// <summary>The service reloaded the new file and every connection that worked before still works.</summary>
    Applied,

    /// <summary>The service rejected the new file, so the previous file was restored.</summary>
    RolledBack,

    /// <summary>The file was saved, but the service did not report back, so the result is unknown.</summary>
    Unverified,
}

/// <summary>
/// Result of saving a config file.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">Summary shown to the user.</param>
/// <param name="Details">Reasons reported by the service.</param>
public sealed record ApplyResult(ApplyOutcome Outcome, string Message, IReadOnlyList<string> Details);

/// <summary>
/// Saves a service config file, waits for the service to reload it, and restores the previous file
/// when the service reports a syntax error or a connection that the new file broke.
/// </summary>
public sealed class ConfigApplier {
    private readonly ConfigStore store;
    private readonly ServiceStatusClient statusClient;
    private readonly TimeSpan reloadTimeout;

    /// <summary>
    /// Initializes a new instance of <see cref="ConfigApplier"/>.
    /// </summary>
    public ConfigApplier(ConfigStore store, ServiceStatusClient statusClient, TimeSpan? reloadTimeout = null) {
        this.store = store;
        this.statusClient = statusClient;
        this.reloadTimeout = reloadTimeout ?? TimeSpan.FromSeconds(8);
    }

    /// <summary>
    /// Saves <paramref name="content"/> as the config file of <paramref name="service"/> and verifies it.
    /// </summary>
    public async Task<ApplyResult> SaveAsync(string service, string content, CancellationToken cancellationToken = default) {
        string previous = store.Read(service);

        if (previous == content) {
            return new(ApplyOutcome.Applied, "內容沒有變更。", []);
        }

        ServiceStatusResult before = await statusClient.GetAsync(service, cancellationToken).ConfigureAwait(false);
        DateTimeOffset savedAt = DateTimeOffset.UtcNow;
        await store.WriteAsync(service, content, cancellationToken).ConfigureAwait(false);

        if (before.Status is null) {
            return new(ApplyOutcome.Unverified, "已儲存，但服務目前沒有回應，無法確認是否生效。", before.Error is null ? [] : [before.Error]);
        }

        ServiceStatus? after = await WaitForReloadAsync(service, savedAt, cancellationToken).ConfigureAwait(false);

        if (after is null) {
            return new(ApplyOutcome.Unverified, "已儲存，但服務沒有在時間內回報重新載入，請稍後查看狀態。", []);
        }

        IReadOnlyList<string> problems = FindRegressions(before.Status, after);

        if (problems.Count == 0) {
            return new(ApplyOutcome.Applied, "已儲存並生效，不需要重啟容器。", []);
        }

        await store.WriteAsync(service, previous, cancellationToken).ConfigureAwait(false);

        return new(ApplyOutcome.RolledBack, "服務無法使用新設定，已還原為上一版。", problems);
    }

    /// <summary>
    /// Lists problems that the new status has and the previous status did not.
    /// </summary>
    public static IReadOnlyList<string> FindRegressions(ServiceStatus before, ServiceStatus after) {
        List<string> problems = [];

        if (after.ConfigFile?.LastError is { Length: > 0 } error) {
            problems.Add(error);
        }

        HashSet<string> workedBefore = (before.Connections ?? [])
            .Where(item => item.State == "loaded")
            .Select(item => item.Alias)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> existedBefore = (before.Connections ?? []).Select(item => item.Alias).ToHashSet(StringComparer.Ordinal);

        foreach (ConnectionLoadStatus item in after.Connections ?? []) {
            bool newlyBroken = item.State != "loaded" && (workedBefore.Contains(item.Alias) || !existedBefore.Contains(item.Alias));

            if (newlyBroken) {
                problems.Add($"{item.Alias}（{item.Name}）：{string.Join(" ", item.Errors)}");
            }
        }

        return problems;
    }

    private async Task<ServiceStatus?> WaitForReloadAsync(string service, DateTimeOffset savedAt, CancellationToken cancellationToken) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + reloadTimeout;

        while (DateTimeOffset.UtcNow < deadline) {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);
            ServiceStatusResult result = await statusClient.GetAsync(service, cancellationToken).ConfigureAwait(false);
            ServiceStatus? status = result.Status;

            if (status is null) {
                continue;
            }

            bool fileError = status.ConfigFile?.LastError is { Length: > 0 };
            bool reloaded = status.ConfigFile?.LastLoadedAt >= savedAt.AddSeconds(-1)
                && (status.ConnectionsLoadedAt is null || status.ConnectionsLoadedAt >= savedAt.AddSeconds(-1));

            if (fileError || reloaded) {
                return status;
            }
        }

        return null;
    }
}
