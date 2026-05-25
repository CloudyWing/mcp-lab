using CloudyWing.McpLab.Shared.DotEnv;
using CloudyWing.McpLab.SqlServer;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class SqlServerConnectionRegistryReloadTests {
    [Test]
    public void Registry_ReloadedSnapshot_ReplacesConnections() {
        TestDotEnvSnapshotProvider provider = new(CreateSnapshot("hostA"));
        ConnectionRegistry registry = new(provider, Substitute.For<ILogger<ConnectionRegistry>>());

        provider.Reload(CreateSnapshot("hostB"));

        using (Assert.EnterMultipleScope()) {
            Assert.That(registry.All["main"].Host, Is.EqualTo("hostB"));
            Assert.That(registry.Get("main").Host, Is.EqualTo("hostB"));
        }
    }

    [Test]
    public void Registry_ReloadedSnapshotWithInvalidAlias_RetainsPreviousConnection() {
        TestDotEnvSnapshotProvider provider = new(CreateSnapshot("hostA"));
        ConnectionRegistry registry = new(provider, Substitute.For<ILogger<ConnectionRegistry>>());
        Dictionary<string, string> invalidSnapshot = CreateSnapshot("hostB");
        invalidSnapshot.Remove("MSSQL_CONN_MAIN_HOST");

        provider.Reload(invalidSnapshot);

        Assert.That(registry.All["main"].Host, Is.EqualTo("hostA"));
    }

    private static Dictionary<string, string> CreateSnapshot(string host) =>
        new(StringComparer.Ordinal) {
            ["MSSQL_CONN_MAIN_NAME"] = "main",
            ["MSSQL_CONN_MAIN_HOST"] = host,
            ["MSSQL_CONN_MAIN_PORT"] = "1433",
            ["MSSQL_CONN_MAIN_USER"] = "sa",
            ["MSSQL_CONN_MAIN_PASSWORD"] = "Password1!",
            ["MSSQL_CONN_MAIN_DATABASE"] = "master",
        };

    private sealed class TestDotEnvSnapshotProvider : IDotEnvSnapshotProvider {
        private IReadOnlyDictionary<string, string> current;

        public TestDotEnvSnapshotProvider(IReadOnlyDictionary<string, string> current) {
            this.current = current;
        }

        public IReadOnlyDictionary<string, string> Current => current;

        public event EventHandler<DotEnvReloadedEventArgs>? Reloaded;

        public void Reload(IReadOnlyDictionary<string, string> next) {
            IReadOnlyDictionary<string, string> previous = current;
            current = next;
            Reloaded?.Invoke(this, new DotEnvReloadedEventArgs(previous, next, [], [], []));
        }
    }
}
