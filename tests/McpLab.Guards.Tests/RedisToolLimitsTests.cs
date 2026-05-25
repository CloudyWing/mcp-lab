using CloudyWing.McpLab.Shared.DotEnv;
using CloudyWing.McpLab.Redis;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class RedisToolLimitsTests {
    [TestCase(0, 100)]
    [TestCase(-1, 100)]
    [TestCase(20, 20)]
    [TestCase(2000, 1000)]
    public void NormalizeKeyCount_RequestValue_ReturnsBoundedValue(int count, int expected) {
        RedisToolLimits limits = new(new TestDotEnvSnapshotProvider(new Dictionary<string, string>(StringComparer.Ordinal)));

        int actual = limits.NormalizeKeyCount(count);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void NormalizeKeyCount_SnapshotDefaultConfigured_UsesConfiguredDefault() {
        RedisToolLimits limits = new(new TestDotEnvSnapshotProvider(new Dictionary<string, string>(StringComparer.Ordinal) {
            ["REDIS_MAX_ROWS"] = "25",
        }));

        int actual = limits.NormalizeKeyCount(0);

        Assert.That(actual, Is.EqualTo(25));
    }

    [TestCase(0, true)]
    [TestCase(30, true)]
    [TestCase(-1, false)]
    public void IsSetKeyTtlValid_Value_ReturnsExpectedResult(int ttlSeconds, bool expected) {
        RedisToolLimits limits = new(new TestDotEnvSnapshotProvider(new Dictionary<string, string>(StringComparer.Ordinal)));

        bool actual = limits.IsSetKeyTtlValid(ttlSeconds);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(0, false)]
    [TestCase(30, true)]
    [TestCase(-1, false)]
    public void IsExpireTtlValid_Value_ReturnsExpectedResult(int ttlSeconds, bool expected) {
        RedisToolLimits limits = new(new TestDotEnvSnapshotProvider(new Dictionary<string, string>(StringComparer.Ordinal)));

        bool actual = limits.IsExpireTtlValid(ttlSeconds);

        Assert.That(actual, Is.EqualTo(expected));
    }

    private sealed class TestDotEnvSnapshotProvider : IDotEnvSnapshotProvider {
        public TestDotEnvSnapshotProvider(IReadOnlyDictionary<string, string> current) {
            Current = current;
        }

        public IReadOnlyDictionary<string, string> Current { get; }

        public event EventHandler<DotEnvReloadedEventArgs>? Reloaded {
            add {
            }
            remove {
            }
        }
    }
}
