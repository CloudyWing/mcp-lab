using System.Text;
using CloudyWing.McpLab.ApiContract;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class SpecSourceClientTests {
    private const string SpecJson = """{"openapi":"3.0.3","info":{"title":"File API","version":"1"},"paths":{"/ping":{"get":{"responses":{"200":{"description":"OK"}}}}}}""";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private string tempDirectory = "";

    [SetUp]
    public void SetUp() {
        tempDirectory = Path.Combine(Path.GetTempPath(), "spec-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    [TearDown]
    public void TearDown() {
        Directory.Delete(tempDirectory, recursive: true);
    }

    [Test]
    public async Task WithSpecAsync_FileSpec_PassesParsedSpecToAction() {
        SpecSourceClient client = CreateClient();
        ConnectionConfig cfg = Config(WriteSpec(SpecJson));

        string title = await client.WithSpecAsync(cfg, spec => spec.Title);

        Assert.That(title, Is.EqualTo("File API"));
    }

    [Test]
    public async Task WithSpecAsync_ConcurrentCalls_RunOneAtATime() {
        SpecSourceClient client = CreateClient();
        ConnectionConfig cfg = Config(WriteSpec(SpecJson));
        int current = 0;
        int maxConcurrent = 0;

        string Observe(OpenApiSpec spec) {
            int now = Interlocked.Increment(ref current);
            int seen;

            while (now > (seen = Volatile.Read(ref maxConcurrent))
                && Interlocked.CompareExchange(ref maxConcurrent, now, seen) != seen
            ) {
            }

            Thread.Sleep(30);
            Interlocked.Decrement(ref current);

            return spec.Title;
        }

        Task<string>[] tasks = Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => client.WithSpecAsync(cfg, Observe)))
            .ToArray();
        await Task.WhenAll(tasks).WaitAsync(Timeout);

        Assert.That(maxConcurrent, Is.EqualTo(1));
    }

    [Test]
    public async Task WithSpecAsync_SecondCallWhileFirstRuns_WaitsUntilFirstCompletes() {
        SpecSourceClient client = CreateClient();
        ConnectionConfig cfg = Config(WriteSpec(SpecJson));
        using ManualResetEventSlim firstEntered = new();
        using ManualResetEventSlim releaseFirst = new();
        bool secondEntered = false;

        Task first = Task.Run(() => client.WithSpecAsync(cfg, spec => {
            firstEntered.Set();
            releaseFirst.Wait(Timeout);

            return spec.Title;
        }));
        Assert.That(firstEntered.Wait(Timeout), Is.True);
        Task second = Task.Run(() => client.WithSpecAsync(cfg, spec => {
            secondEntered = true;

            return spec.Title;
        }));

        await Task.Delay(200);
        bool enteredWhileFirstHeldGate = Volatile.Read(ref secondEntered);
        releaseFirst.Set();
        await Task.WhenAll(first, second).WaitAsync(Timeout);

        using (Assert.EnterMultipleScope()) {
            Assert.That(enteredWhileFirstHeldGate, Is.False);
            Assert.That(secondEntered, Is.True);
        }
    }

    [Test]
    public async Task WithSpecAsync_CancelledWhileWaiting_ThrowsAndLeavesGateUsable() {
        SpecSourceClient client = CreateClient();
        ConnectionConfig cfg = Config(WriteSpec(SpecJson));
        using ManualResetEventSlim firstEntered = new();
        using ManualResetEventSlim releaseFirst = new();
        using CancellationTokenSource cts = new();

        Task first = Task.Run(() => client.WithSpecAsync(cfg, spec => {
            firstEntered.Set();
            releaseFirst.Wait(Timeout);

            return spec.Title;
        }));
        Assert.That(firstEntered.Wait(Timeout), Is.True);
        Task<string> waiting = client.WithSpecAsync(cfg, spec => spec.Title, cts.Token);
        await cts.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await waiting);
        releaseFirst.Set();
        await first.WaitAsync(Timeout);
        string title = await client.WithSpecAsync(cfg, spec => spec.Title).WaitAsync(Timeout);

        Assert.That(title, Is.EqualTo("File API"));
    }

    [Test]
    public async Task WithSpecAsync_ActionThrows_ReleasesGate() {
        SpecSourceClient client = CreateClient();
        ConnectionConfig cfg = Config(WriteSpec(SpecJson));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.WithSpecAsync<string>(cfg, _ => throw new InvalidOperationException("boom"))
        );
        string title = await client.WithSpecAsync(cfg, spec => spec.Title).WaitAsync(Timeout);

        Assert.That(title, Is.EqualTo("File API"));
    }

    [Test]
    public async Task WithSpecAsync_LoadFails_ReleasesGate() {
        SpecSourceClient client = CreateClient();
        ConnectionConfig missing = Config(Path.Combine(tempDirectory, "missing.json"));
        ConnectionConfig valid = Config(WriteSpec(SpecJson));

        Assert.ThrowsAsync<FileNotFoundException>(async () => await client.WithSpecAsync(missing, spec => spec.Title));
        string title = await client.WithSpecAsync(valid, spec => spec.Title).WaitAsync(Timeout);

        Assert.That(title, Is.EqualTo("File API"));
    }

    [Test]
    public void WithSpecAsync_FileLargerThanLimit_ThrowsExceedsError() {
        SpecSourceClient client = CreateClient();
        string path = Path.Combine(tempDirectory, "huge.json");
        using (FileStream stream = File.Create(path)) {
            stream.SetLength(SpecSourceClient.MaxSpecBytes + 1L);
        }

        InvalidOperationException? ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.WithSpecAsync(Config(path), spec => spec.Title)
        );

        Assert.That(ex!.Message, Does.Contain("exceeds"));
    }

    [Test]
    public async Task WithSpecAsync_FileWithUtf8Bom_ParsesSpec() {
        SpecSourceClient client = CreateClient();
        string path = Path.Combine(tempDirectory, "bom.json");
        await File.WriteAllTextAsync(path, SpecJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        string title = await client.WithSpecAsync(Config(path), spec => spec.Title);

        Assert.That(title, Is.EqualTo("File API"));
    }

    private string WriteSpec(string content) {
        string path = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, content);

        return path;
    }

    private static ConnectionConfig Config(string specPath) =>
        new(
            "test",
            "",
            specPath,
            "",
            new HashSet<string>(),
            false,
            false,
            "",
            ""
        );

    private static SpecSourceClient CreateClient() {
        ConnectionRegistry registry = new(
            Substitute.For<IHttpClientFactory>(),
            new EmptyDotEnvSnapshotProvider(),
            Substitute.For<ILogger<ConnectionRegistry>>()
        );

        return new SpecSourceClient(registry);
    }

    private sealed class EmptyDotEnvSnapshotProvider : IDotEnvSnapshotProvider {
        public IReadOnlyDictionary<string, string> Current { get; } = new Dictionary<string, string>();

        public event EventHandler<DotEnvReloadedEventArgs>? Reloaded {
            add {
            }
            remove {
            }
        }
    }
}
