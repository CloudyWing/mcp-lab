using System.Text;
using System.Text.Json.Nodes;
using CloudyWing.McpLab.Mailpit;
using CloudyWing.McpLab.Shared;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class MailpitSourceFormatterTests {
    [Test]
    public void Format_CompleteBodyWithinLimit_KeepsExistingFieldsOnly() {
        BoundedBody body = Complete("Subject: hi\nbody");

        JsonNode data = ParseData(MailpitSourceFormatter.Format("abc", 100, body, 1000));

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                data.ToJsonString(),
                Is.EqualTo("""{"id":"abc","limit":100,"source_length":16,"truncated":false,"source":"Subject: hi\nbody"}""")
            );
            Assert.That(data["original_bytes"], Is.Null);
            Assert.That(data["max_response_bytes"], Is.Null);
        }
    }

    [Test]
    public void Format_CompleteBodyOverCharacterLimit_TruncatesSourceWithoutBudgetFields() {
        BoundedBody body = Complete("0123456789");

        JsonNode data = ParseData(MailpitSourceFormatter.Format("abc", 4, body, 1000));

        using (Assert.EnterMultipleScope()) {
            Assert.That(data["source"]!.GetValue<string>(), Is.EqualTo("0123"));
            Assert.That(data["source_length"]!.GetValue<long>(), Is.EqualTo(10));
            Assert.That(data["truncated"]!.GetValue<bool>(), Is.True);
            Assert.That(data["original_bytes"], Is.Null);
        }
    }

    [Test]
    public void Format_BodyOverResponseLimit_ReportsOriginalBytesAndTruncationFields() {
        BoundedBody body = new(Encoding.UTF8.GetBytes("abcdef"), true, 5000, true);

        string json = MailpitSourceFormatter.Format("abc", 100, body, 6);
        JsonNode root = JsonNode.Parse(json)!;
        JsonNode data = root["data"]!;

        using (Assert.EnterMultipleScope()) {
            Assert.That(root["ok"]!.GetValue<bool>(), Is.True);
            Assert.That(root["message"]!.GetValue<string>(), Does.Contain("MCPLAB_MAX_RESPONSE_BYTES"));
            Assert.That(data["source"]!.GetValue<string>(), Is.EqualTo("abcdef"));
            Assert.That(data["truncated"]!.GetValue<bool>(), Is.True);
            Assert.That(data["source_length"]!.GetValue<long>(), Is.EqualTo(5000));
            Assert.That(data["original_bytes"]!.GetValue<long>(), Is.EqualTo(5000));
            Assert.That(data["original_bytes_exact"]!.GetValue<bool>(), Is.True);
            Assert.That(data["max_response_bytes"]!.GetValue<int>(), Is.EqualTo(6));
        }
    }

    [Test]
    public void Format_BodyOverResponseLimitWithUnknownSize_ReportsInexactSize() {
        BoundedBody body = new(Encoding.UTF8.GetBytes("abcdef"), true, 7, false);

        JsonNode data = ParseData(MailpitSourceFormatter.Format("abc", 100, body, 6));

        using (Assert.EnterMultipleScope()) {
            Assert.That(data["source_length"]!.GetValue<long>(), Is.EqualTo(7));
            Assert.That(data["original_bytes_exact"]!.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public void Format_BothLimitsApply_SourceFollowsSmallerLimitAndSizeFollowsBody() {
        BoundedBody body = new(Encoding.UTF8.GetBytes("0123456789"), true, 99999, true);

        JsonNode data = ParseData(MailpitSourceFormatter.Format("abc", 4, body, 10));

        using (Assert.EnterMultipleScope()) {
            Assert.That(data["source"]!.GetValue<string>(), Is.EqualTo("0123"));
            Assert.That(data["limit"]!.GetValue<int>(), Is.EqualTo(4));
            Assert.That(data["truncated"]!.GetValue<bool>(), Is.True);
            Assert.That(data["source_length"]!.GetValue<long>(), Is.EqualTo(99999));
            Assert.That(data["original_bytes"]!.GetValue<long>(), Is.EqualTo(99999));
        }
    }

    [Test]
    public void Format_BodyCutInMultibyteCharacter_DropsIncompleteSequence() {
        byte[] full = Encoding.UTF8.GetBytes("中文");
        BoundedBody body = new(full[..^1], true, full.Length, true);

        JsonNode data = ParseData(MailpitSourceFormatter.Format("abc", 100, body, full.Length - 1));

        Assert.That(data["source"]!.GetValue<string>(), Is.EqualTo("中"));
    }

    private static BoundedBody Complete(string text) {
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        return new BoundedBody(bytes, false, bytes.Length, true);
    }

    private static JsonNode ParseData(string json) => JsonNode.Parse(json)!["data"]!;
}
