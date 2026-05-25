using Microsoft.Extensions.Logging;

namespace CloudyWing.McpLab.Shared.DotEnv;

/// <summary>
/// Watches a .env file and reloads the snapshot after changes.
/// </summary>
public sealed partial class DotEnvWatcher : IDisposable {
    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromSeconds(1);
    private readonly DotEnvFileSource source;
    private readonly DotEnvSnapshotProvider snapshotProvider;
    private readonly ILogger<DotEnvWatcher> logger;
    private readonly TimeSpan debounceDelay;
    private readonly TimeSpan pollingInterval;
    private readonly SemaphoreSlim reloadGate = new(1, 1);
    private readonly object syncRoot = new();
    private FileSystemWatcher? fileSystemWatcher;
    private Timer? debounceTimer;
    private CancellationTokenSource? pollingCancellationTokenSource;
    private Task? pollingTask;
    private DotEnvFileFingerprint lastFingerprint = new(false, DateTime.MinValue, 0);
    private bool disposed;
    private bool started;

    /// <summary>
    /// Initializes a new instance of <see cref="DotEnvWatcher"/>.
    /// </summary>
    /// <param name="source">The .env file source.</param>
    /// <param name="snapshotProvider">The snapshot provider to reload.</param>
    /// <param name="logger">The logger.</param>
    public DotEnvWatcher(
        DotEnvFileSource source,
        DotEnvSnapshotProvider snapshotProvider,
        ILogger<DotEnvWatcher> logger
    ) : this(source, snapshotProvider, logger, DefaultDebounceDelay, DefaultPollingInterval) {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DotEnvWatcher"/> with custom timings.
    /// </summary>
    /// <param name="source">The .env file source.</param>
    /// <param name="snapshotProvider">The snapshot provider to reload.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="debounceDelay">The debounce delay for file system events.</param>
    /// <param name="pollingInterval">The fallback polling interval.</param>
    public DotEnvWatcher(
        DotEnvFileSource source,
        DotEnvSnapshotProvider snapshotProvider,
        ILogger<DotEnvWatcher> logger,
        TimeSpan debounceDelay,
        TimeSpan pollingInterval
    ) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.source = source;
        this.snapshotProvider = snapshotProvider;
        this.logger = logger;
        this.debounceDelay = debounceDelay;
        this.pollingInterval = pollingInterval;
    }

    /// <summary>
    /// Starts watching the .env file.
    /// </summary>
    public void Start() {
        ThrowIfDisposed();

        lock (syncRoot) {
            if (started) {
                return;
            }

            started = true;
            lastFingerprint = source.GetFingerprint();
            ReloadWithWarning();
            StartFileSystemWatcher();
            StartPolling();
        }
    }

    /// <summary>
    /// Stops watching the .env file.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels waiting for the polling task.</param>
    /// <returns>A task that completes after the watcher stops.</returns>
    public async Task StopAsync(CancellationToken cancellationToken) {
        CancellationTokenSource? pollingCts;
        Task? task;

        lock (syncRoot) {
            if (!started) {
                return;
            }

            started = false;
            fileSystemWatcher?.Dispose();
            fileSystemWatcher = null;
            debounceTimer?.Dispose();
            debounceTimer = null;
            pollingCts = pollingCancellationTokenSource;
            pollingCancellationTokenSource = null;
            task = pollingTask;
            pollingTask = null;
        }

        if (pollingCts is not null) {
            await pollingCts.CancelAsync().ConfigureAwait(false);
            pollingCts.Dispose();
        }

        if (task is not null) {
            try {
                await task.WaitAsync(cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (disposed) {
            return;
        }

        disposed = true;

        lock (syncRoot) {
            fileSystemWatcher?.Dispose();
            debounceTimer?.Dispose();
            pollingCancellationTokenSource?.Cancel();
            pollingCancellationTokenSource?.Dispose();
        }

        reloadGate.Dispose();
    }

    private void StartFileSystemWatcher() {
        string? directory = Path.GetDirectoryName(source.Path);
        string fileName = Path.GetFileName(source.Path);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName) || !Directory.Exists(directory)) {
            return;
        }

        try {
            fileSystemWatcher = new FileSystemWatcher(directory, fileName) {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            fileSystemWatcher.Changed += HandleFileChanged;
            fileSystemWatcher.Created += HandleFileChanged;
            fileSystemWatcher.Deleted += HandleFileChanged;
            fileSystemWatcher.Renamed += HandleFileChanged;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) {
            LogFileSystemWatcherUnavailable(logger, ex.Message);
        }
    }

    private void StartPolling() {
        CancellationTokenSource pollingCts = new();
        pollingCancellationTokenSource = pollingCts;
        pollingTask = PollAsync(pollingCts.Token);
    }

    private async Task PollAsync(CancellationToken cancellationToken) {
        using PeriodicTimer timer = new(pollingInterval);

        try {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
                DotEnvFileFingerprint currentFingerprint = source.GetFingerprint();

                if (currentFingerprint != lastFingerprint) {
                    lastFingerprint = currentFingerprint;
                    ScheduleReload();
                }
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        } catch (Exception ex) {
            LogPollingFailed(logger, ex.Message);
        }
    }

    private void HandleFileChanged(object sender, FileSystemEventArgs e) {
        lastFingerprint = source.GetFingerprint();
        ScheduleReload();
    }

    private void ScheduleReload() {
        lock (syncRoot) {
            debounceTimer ??= new Timer(_ => _ = ReloadWithRetryAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            debounceTimer.Change(debounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task ReloadWithRetryAsync() {
        await reloadGate.WaitAsync().ConfigureAwait(false);

        try {
            if (snapshotProvider.TryReload(out string? firstError)) {
                return;
            }

            await Task.Delay(debounceDelay).ConfigureAwait(false);

            if (!snapshotProvider.TryReload(out string? secondError)) {
                LogReloadFailed(logger, secondError ?? firstError ?? "Unknown reload error.");
            }
        } finally {
            reloadGate.Release();
        }
    }

    private void ReloadWithWarning() {
        if (!snapshotProvider.TryReload(out string? error)) {
            LogReloadFailed(logger, error ?? "Unknown reload error.");
        }
    }

    private void ThrowIfDisposed() {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = ".env file system watcher unavailable; polling fallback remains active. Reason: {Reason}"
    )]
    private static partial void LogFileSystemWatcherUnavailable(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Warning,
        Message = ".env polling failed. Reason: {Reason}"
    )]
    private static partial void LogPollingFailed(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Warning,
        Message = ".env parse failed: {Reason}"
    )]
    private static partial void LogReloadFailed(ILogger logger, string reason);
}
