using CloudyWing.McpLab.Shared;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class SensitiveDataSanitizerTests {
    [TestCase("Password=sample-secret;User Id=sa", "Password=***;User Id=sa")]
    [TestCase("token: abcdef, host=localhost", "token: ***, host=localhost")]
    [TestCase("https://user:secret@example.test/path", "https://***:***@example.test/path")]
    [TestCase("""{"password":"sample-secret","host":"localhost"}""", """{"password":"***","host":"localhost"}""")]
    public void Redact_MessageWithSecret_ReturnsRedactedMessage(string value, string expected) {
        string actual = SensitiveDataSanitizer.Redact(value);

        Assert.That(actual, Is.EqualTo(expected));
    }
}
