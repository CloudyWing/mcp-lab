using System.Text.Json.Nodes;

using CloudyWing.McpLab.Shared;

using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class WriteConnectionGuardTests {
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void CheckMissing_EmptyConnection_ReturnsBlockedResponse(string? connection) {
        string? response = WriteConnectionGuard.CheckMissing(connection);

        Assert.That(response, Is.Not.Null);
        JsonNode node = JsonNode.Parse(response!)!;
        Assert.Multiple(() => {
            Assert.That(node["ok"]!.GetValue<bool>(), Is.False);
            Assert.That(node["kind"]!.GetValue<string>(), Is.EqualTo("blocked"));
            Assert.That(node["message"]!.GetValue<string>(), Does.Contain("explicit connection name"));
        });
    }

    [Test]
    public void CheckMissing_NamedConnection_ReturnsNull() {
        Assert.That(WriteConnectionGuard.CheckMissing("staging"), Is.Null);
    }
}
