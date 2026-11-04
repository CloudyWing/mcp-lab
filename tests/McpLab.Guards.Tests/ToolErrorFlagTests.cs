using CloudyWing.McpLab.Shared;

using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ToolErrorFlagTests {
    [Test]
    public void IsFailureEnvelope_ErrorAndBlocked_ReturnTrue() {
        Assert.Multiple(() => {
            Assert.That(ToolErrorFlag.IsFailureEnvelope(ToolResponse.Error("boom")), Is.True);
            Assert.That(ToolErrorFlag.IsFailureEnvelope(ToolResponse.Blocked("no")), Is.True);
        });
    }

    [TestCase("""{"ok":true,"kind":"ok","message":"OK","data":null}""")]
    [TestCase("""{"ok":true,"kind":"empty","message":"none","data":[]}""")]
    [TestCase("""{"ok":false}""")]
    [TestCase("""[{"ok":false,"kind":"error"}]""")]
    [TestCase("plain text")]
    [TestCase("{not json")]
    public void IsFailureEnvelope_OtherText_ReturnsFalse(string text) {
        Assert.That(ToolErrorFlag.IsFailureEnvelope(text), Is.False);
    }
}
