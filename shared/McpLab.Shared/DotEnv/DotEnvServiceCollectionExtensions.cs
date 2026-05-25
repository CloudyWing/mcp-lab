using Microsoft.Extensions.DependencyInjection;

namespace CloudyWing.McpLab.Shared.DotEnv;

/// <summary>
/// Provides dependency injection registrations for .env snapshot services.
/// </summary>
public static class DotEnvServiceCollectionExtensions {
    /// <summary>
    /// Registers .env snapshot and watcher services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="path">The .env file path.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddDotEnvSnapshot(this IServiceCollection services, string path) {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        services.AddSingleton(new DotEnvFileSource(path));
        services.AddSingleton<DotEnvSnapshotProvider>();
        services.AddSingleton<IDotEnvSnapshotProvider>(
            provider => provider.GetRequiredService<DotEnvSnapshotProvider>()
        );
        services.AddSingleton<DotEnvWatcher>();
        services.AddHostedService<DotEnvHostedService>();

        return services;
    }
}
