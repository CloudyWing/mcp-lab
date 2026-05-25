using Microsoft.Extensions.Hosting;

namespace CloudyWing.McpLab.Shared.DotEnv;

/// <summary>
/// Manages the lifetime of the .env file watcher.
/// </summary>
public sealed class DotEnvHostedService : IHostedService {
    private readonly DotEnvWatcher watcher;

    /// <summary>
    /// Initializes a new instance of <see cref="DotEnvHostedService"/>.
    /// </summary>
    /// <param name="watcher">The .env file watcher.</param>
    public DotEnvHostedService(DotEnvWatcher watcher) {
        ArgumentNullException.ThrowIfNull(watcher);
        this.watcher = watcher;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) {
        watcher.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => watcher.StopAsync(cancellationToken);
}
