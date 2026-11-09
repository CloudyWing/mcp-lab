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
    public void Error_ResponseTooLargeException_AttachesTruncationData() {
        string json = ToolResponse.Error(new ResponseTooLargeException(5000, false, 1000));

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement data = root.GetProperty("data");

        using (Assert.EnterMultipleScope()) {
            Assert.That(root.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("error"));
            Assert.That(root.GetProperty("message").GetString(), Does.Contain("MCPLAB_MAX_RESPONSE_BYTES").And.Contain("1000"));
            Assert.That(data.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("original_bytes").GetInt64(), Is.EqualTo(5000));
            Assert.That(data.GetProperty("original_bytes_exact").GetBoolean(), Is.False);
            Assert.That(data.GetProperty("max_response_bytes").GetInt32(), Is.EqualTo(1000));
        }
    }

    [Test]
    public void Error_OtherException_HasNoData() {
        string json = ToolResponse.Error(new InvalidOperationException("boom"));

        using JsonDocument document = JsonDocument.Parse(json);

        Assert.That(document.RootElement.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void Ok_Data_DoesNotContainTruncationFields() {
        string json = ToolResponse.Ok(new { value = 1 });

        Assert.That(json, Does.Not.Contain("original_bytes").And.Not.Contain("max_response_bytes").And.Not.Contain("truncated"));
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

    [Test]
    public void Ok_ChineseText_WritesOriginalCharacters() {
        string json = ToolResponse.Ok(new { v = "中文" });

        using JsonDocument document = JsonDocument.Parse(json);

        using (Assert.EnterMultipleScope()) {
            Assert.That(json, Does.Contain("中文"));
            Assert.That(json, Does.Not.Contain("\\u4E2D").And.Not.Contain("\\u4e2d"));
            Assert.That(document.RootElement.GetProperty("data").GetProperty("v").GetString(), Is.EqualTo("中文"));
        }
    }

    [Test]
    public void Ok_HtmlSensitiveCharactersAndEmoji_RoundTripsOriginalText() {
        string text = "<tag> & 'quote' + `tick` 😀";

        string json = ToolResponse.Ok(new { v = text });

        using JsonDocument document = JsonDocument.Parse(json);

        using (Assert.EnterMultipleScope()) {
            Assert.That(document.RootElement.GetProperty("data").GetProperty("v").GetString(), Is.EqualTo(text));
            Assert.That(json, Does.Contain("<tag> & 'quote' + `tick`"));
        }
    }

    [Test]
    public void Ok_AsciiWithoutSensitiveCharacters_ReturnsExactLiteral() {
        string json = ToolResponse.Ok(new { value = "abc123" });

        Assert.That(json, Is.EqualTo("""{"ok":true,"kind":"ok","message":"OK","data":{"value":"abc123"}}"""));
    }

    [Test]
    public void Error_ChineseMessageWithSecret_RedactsSecretAndKeepsText() {
        string json = ToolResponse.Error("Password=sample-secret; 登入失敗");

        using JsonDocument document = JsonDocument.Parse(json);

        Assert.That(document.RootElement.GetProperty("message").GetString(), Is.EqualTo("Password=***; 登入失敗"));
    }

    [Test]
    public void IsFailureEnvelope_ChineseErrorMessage_ReturnsTrue() {
        Assert.That(ToolErrorFlag.IsFailureEnvelope(ToolResponse.Error("中文錯誤")), Is.True);
    }
}
