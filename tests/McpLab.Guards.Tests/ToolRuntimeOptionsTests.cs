using CloudyWing.McpLab.Shared;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ToolRuntimeOptionsTests {
    [TearDown]
    public void TearDown() {
        Environment.SetEnvironmentVariable("MCP_LAB_TEST_PRIMARY", null);
        Environment.SetEnvironmentVariable("MCP_LAB_TEST_FALLBACK", null);
    }

    [TestCase(null, 10)]
    [TestCase("", 10)]
    [TestCase("abc", 10)]
    [TestCase("0", 10)]
    [TestCase("-1", 10)]
    [TestCase("20", 20)]
    [TestCase("999", 100)]
    public void NormalizeInt32_ConfigValue_ReturnsBoundedValue(string? value, int expected) {
        int actual = ToolRuntimeOptions.NormalizeInt32(value, 10, 1, 100);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(0, 10)]
    [TestCase(-1, 10)]
    [TestCase(20, 20)]
    [TestCase(999, 100)]
    public void NormalizeRequestedInt32_RequestValue_ReturnsBoundedValue(int value, int expected) {
        int actual = ToolRuntimeOptions.NormalizeRequestedInt32(value, 10, 1, 100);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void GetEnvironmentInt32_PrimaryConfigured_ReturnsPrimaryValue() {
        Environment.SetEnvironmentVariable("MCP_LAB_TEST_PRIMARY", "20");
        Environment.SetEnvironmentVariable("MCP_LAB_TEST_FALLBACK", "30");

        int actual = ToolRuntimeOptions.GetEnvironmentInt32(
            ["MCP_LAB_TEST_PRIMARY", "MCP_LAB_TEST_FALLBACK"],
            10,
            1,
            100
        );

        Assert.That(actual, Is.EqualTo(20));
    }

    [Test]
    public void GetEnvironmentInt32_PrimaryMissing_ReturnsFallbackValue() {
        Environment.SetEnvironmentVariable("MCP_LAB_TEST_FALLBACK", "30");

        int actual = ToolRuntimeOptions.GetEnvironmentInt32(
            ["MCP_LAB_TEST_PRIMARY", "MCP_LAB_TEST_FALLBACK"],
            10,
            1,
            100
        );

        Assert.That(actual, Is.EqualTo(30));
    }

    [Test]
    public void NormalizeInt32_DefaultOutsideBounds_ThrowsArgumentOutOfRangeException() {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ToolRuntimeOptions.NormalizeInt32("5", 0, 1, 100)
        );
    }

    [Test]
    public void NormalizeInt32_MinGreaterThanMax_ThrowsArgumentException() {
        Assert.Throws<ArgumentException>(
            () => ToolRuntimeOptions.NormalizeInt32("5", 10, 100, 1)
        );
    }
}
