using CloudyWing.McpLab.Shared;

using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class McpAccessTokenTests {
    [TestCase("Bearer secret")]
    [TestCase("bearer  secret ")]
    public void IsAuthorized_MatchingBearerToken_ReturnsTrue(string header) {
        Assert.That(McpAccessToken.IsAuthorized(header, "secret"), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Bearer other")]
    [TestCase("Basic secret")]
    [TestCase("secret")]
    public void IsAuthorized_MissingOrWrongToken_ReturnsFalse(string? header) {
        Assert.That(McpAccessToken.IsAuthorized(header, "secret"), Is.False);
    }

    [Test]
    public void IsAuthorized_NoExpectedToken_ReturnsFalse() {
        Assert.That(McpAccessToken.IsAuthorized("Bearer ", ""), Is.False);
    }

    [Test]
    public void GetExpectedToken_TokenNotConfigured_ReturnsEmpty() {
        Assert.That(McpAccessToken.GetExpectedToken(new Dictionary<string, string>()), Is.Empty);
    }

    [Test]
    public void GetExpectedToken_TokenConfigured_ReturnsTrimmedValue() {
        Dictionary<string, string> snapshot = new() {
            [McpAccessToken.VariableName] = " secret ",
        };

        Assert.That(McpAccessToken.GetExpectedToken(snapshot), Is.EqualTo("secret"));
    }
}
