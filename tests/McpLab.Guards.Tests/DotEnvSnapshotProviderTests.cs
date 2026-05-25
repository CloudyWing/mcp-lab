using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class DotEnvSnapshotProviderTests {
    [Test]
    public void TryReload_ChangedFile_ReplacesSnapshotAndRaisesEvent() {
        string path = CreateTempFile("KEY=one");
        DotEnvSnapshotProvider provider = CreateProvider(path);
        DotEnvReloadedEventArgs? eventArgs = null;
        provider.Reloaded += (_, args) => eventArgs = args;

        bool firstReload = provider.TryReload(out string? firstError);
        File.WriteAllText(path, "KEY=two\nNEXT=value");
        bool secondReload = provider.TryReload(out string? secondError);

        using (Assert.EnterMultipleScope()) {
            Assert.That(firstReload, Is.True);
            Assert.That(firstError, Is.Null);
            Assert.That(secondReload, Is.True);
            Assert.That(secondError, Is.Null);
            Assert.That(provider.Current["KEY"], Is.EqualTo("two"));
            Assert.That(provider.Current["NEXT"], Is.EqualTo("value"));
            Assert.That(eventArgs, Is.Not.Null);
            Assert.That(eventArgs!.ChangedKeys, Is.EquivalentTo(new[] { "KEY" }));
            Assert.That(eventArgs.AddedKeys, Is.EquivalentTo(new[] { "NEXT" }));
        }
    }

    [Test]
    public void TryReload_InvalidFile_RetainsPreviousSnapshot() {
        string path = CreateTempFile("KEY=one");
        DotEnvSnapshotProvider provider = CreateProvider(path);
        bool firstReload = provider.TryReload(out string? firstError);
        File.WriteAllText(path, "KEY=\"unterminated");

        bool secondReload = provider.TryReload(out string? secondError);

        using (Assert.EnterMultipleScope()) {
            Assert.That(firstReload, Is.True);
            Assert.That(firstError, Is.Null);
            Assert.That(secondReload, Is.False);
            Assert.That(secondError, Is.Not.Null);
            Assert.That(provider.Current["KEY"], Is.EqualTo("one"));
        }
    }

    [Test]
    public void TryReload_FileOmitsBaselineKey_RetainsBaselineValue() {
        string path = CreateTempFile("KEY=file");
        DotEnvSnapshotProvider provider = CreateProvider(
            path,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                ["KEY"] = "baseline",
                ["BASELINE_ONLY"] = "value"
            }
        );

        bool reloaded = provider.TryReload(out string? error);

        using (Assert.EnterMultipleScope()) {
            Assert.That(reloaded, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(provider.Current["KEY"], Is.EqualTo("file"));
            Assert.That(provider.Current["BASELINE_ONLY"], Is.EqualTo("value"));
        }
    }

    [Test]
    public void TryReload_RemovedFileKey_FallsBackToBaselineValue() {
        string path = CreateTempFile("KEY=file");
        DotEnvSnapshotProvider provider = CreateProvider(
            path,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                ["KEY"] = "baseline"
            }
        );
        bool firstReload = provider.TryReload(out string? firstError);
        File.WriteAllText(path, "");

        bool secondReload = provider.TryReload(out string? secondError);

        using (Assert.EnterMultipleScope()) {
            Assert.That(firstReload, Is.True);
            Assert.That(firstError, Is.Null);
            Assert.That(secondReload, Is.True);
            Assert.That(secondError, Is.Null);
            Assert.That(provider.Current["KEY"], Is.EqualTo("baseline"));
        }
    }

    private static DotEnvSnapshotProvider CreateProvider(
        string path,
        IReadOnlyDictionary<string, string>? baseline = null
    ) =>
        new(
            new DotEnvFileSource(path),
            Substitute.For<ILogger<DotEnvSnapshotProvider>>(),
            baseline ?? new Dictionary<string, string>(StringComparer.Ordinal)
        );

    private static string CreateTempFile(string content) {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.env");
        File.WriteAllText(path, content);
        return path;
    }
}
