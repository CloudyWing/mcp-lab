using System.Text.Json.Nodes;
using CloudyWing.McpLab.Mailpit;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class MailpitMessageMatcherTests {
    [Test]
    public void Matches_SubjectAndRecipientMatch_ReturnsTrue() {
        JsonNode message = JsonNode.Parse(
            """
            {
              "Subject": "Welcome to MCP Lab",
              "To": [
                { "Name": "Wing", "Address": "wing@example.test" }
              ]
            }
            """
        )!;

        bool actual = MailpitMessageMatcher.Matches(message, "wing@example.test", "mcp lab");

        Assert.That(actual, Is.True);
    }

    [Test]
    public void Matches_RecipientDoesNotMatch_ReturnsFalse() {
        JsonNode message = JsonNode.Parse(
            """
            {
              "Subject": "Welcome to MCP Lab",
              "To": [
                { "Name": "Wing", "Address": "wing@example.test" }
              ]
            }
            """
        )!;

        bool actual = MailpitMessageMatcher.Matches(message, "other@example.test", "mcp lab");

        Assert.That(actual, Is.False);
    }
}
