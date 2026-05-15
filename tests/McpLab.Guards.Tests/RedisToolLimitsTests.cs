using CloudyWing.McpLab.Redis;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class RedisToolLimitsTests {
    [TestCase(0, 100)]
    [TestCase(-1, 100)]
    [TestCase(20, 20)]
    [TestCase(2000, 1000)]
    public void NormalizeKeyCount_RequestValue_ReturnsBoundedValue(int count, int expected) {
        int actual = RedisToolLimits.NormalizeKeyCount(count);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(0, true)]
    [TestCase(30, true)]
    [TestCase(-1, false)]
    public void IsSetKeyTtlValid_Value_ReturnsExpectedResult(int ttlSeconds, bool expected) {
        bool actual = RedisToolLimits.IsSetKeyTtlValid(ttlSeconds);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(0, false)]
    [TestCase(30, true)]
    [TestCase(-1, false)]
    public void IsExpireTtlValid_Value_ReturnsExpectedResult(int ttlSeconds, bool expected) {
        bool actual = RedisToolLimits.IsExpireTtlValid(ttlSeconds);

        Assert.That(actual, Is.EqualTo(expected));
    }
}
