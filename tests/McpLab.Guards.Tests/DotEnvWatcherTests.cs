using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class DotEnvWatcherTests {
    [Test]
    public async Task Watcher_FileChanged_ReloadsDebouncedSnapshotAsync() {
        string path = CreateTempFile("WATCHED=one");
        DotEnvSnapshotProvider provider = new(
            new DotEnvFileSource(path),
            Substitute.For<ILogger<DotEnvSnapshotProvider>>()
        );
        using DotEnvWatcher watcher = new(
            new DotEnvFileSource(path),
            provider,
            Substitute.For<ILogger<DotEnvWatcher>>(),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50)
        );
        watcher.Start();
        await WaitUntilAsync(() => provider.Current.GetValueOrDefault("WATCHED") == "one").ConfigureAwait(false);

        File.WriteAllText(path, "WATCHED=two");
        File.WriteAllText(path, "WATCHED=three");

        await WaitUntilAsync(() => provider.Current.GetValueOrDefault("WATCHED") == "three").ConfigureAwait(false);
        await watcher.StopAsync(CancellationToken.None).ConfigureAwait(false);

        Assert.That(provider.Current["WATCHED"], Is.EqualTo("three"));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate) {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));

        while (!predicate()) {
            await Task.Delay(TimeSpan.FromMilliseconds(25), cts.Token).ConfigureAwait(false);
        }
    }

    private static string CreateTempFile(string content) {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.env");
        File.WriteAllText(path, content);
        return path;
    }
}
