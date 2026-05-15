using System.Text.Json;
using CloudyWing.McpLab.Shared;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ToolResponseTests {
    [Test]
    public void Ok_Data_ReturnsSuccessEnvelope() {
        string json = ToolResponse.Ok(new { value = 1 });

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        using (Assert.EnterMultipleScope()) {
            Assert.That(root.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("ok"));
            Assert.That(root.GetProperty("data").GetProperty("value").GetInt32(), Is.EqualTo(1));
        }
    }

    [Test]
    public void Blocked_Message_ReturnsBlockedEnvelope() {
        string json = ToolResponse.Blocked("DROP TABLE is not allowed.");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        using (Assert.EnterMultipleScope()) {
            Assert.That(root.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("blocked"));
            Assert.That(root.GetProperty("message").GetString(), Does.Contain("DROP TABLE"));
        }
    }

    [Test]
    public void Error_MessageWithSecret_RedactsSecret() {
        string json = ToolResponse.Error("Password=sample-secret; login failed");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.That(root.GetProperty("message").GetString(), Is.EqualTo("Password=***; login failed"));
    }

    [Test]
    public void Error_DataWithSecret_RedactsSecretRecursively() {
        string json = ToolResponse.Error(
            "failed",
            new {
                password = "sample-secret",
                nested = new {
                    token = "sample-token",
                    url = "https://user:secret@example.test/path",
                },
            }
        );

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement data = document.RootElement.GetProperty("data");

        using (Assert.EnterMultipleScope()) {
            Assert.That(data.GetProperty("password").GetString(), Is.EqualTo("***"));
            Assert.That(data.GetProperty("nested").GetProperty("token").GetString(), Is.EqualTo("***"));
            Assert.That(
                data.GetProperty("nested").GetProperty("url").GetString(),
                Is.EqualTo("https://***:***@example.test/path")
            );
        }
    }

    [Test]
    public void Ok_DataWithNestedArrays_ReturnsEnvelope() {
        string json = ToolResponse.Ok(new {
            columns = new[] { "value" },
            rows = new[] {
                new[] { "1" },
            },
        });

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement data = document.RootElement.GetProperty("data");

        using (Assert.EnterMultipleScope()) {
            Assert.That(data.GetProperty("columns")[0].GetString(), Is.EqualTo("value"));
            Assert.That(data.GetProperty("rows")[0][0].GetString(), Is.EqualTo("1"));
        }
    }
}
