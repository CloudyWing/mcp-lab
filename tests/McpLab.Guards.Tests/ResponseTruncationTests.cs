using System.Text.Json;
using System.Text.Json.Nodes;
using CloudyWing.McpLab.Shared;

using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ResponseTruncationTests {
    [Test]
    public void Append_ExistingFields_KeepsOrderAndOverridesTruncated() {
        JsonObject result = ResponseTruncation.Append(new { rows = new[] { 1, 2 }, truncated = false, count = 2 }, 5000, true, 1000);

        Assert.That(
            result.Select(property => property.Key),
            Is.EqualTo(new[] { "rows", "truncated", "count", "original_bytes", "original_bytes_exact", "max_response_bytes" })
        );
        Assert.That(result["truncated"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void Append_ReturnsFieldsWithDocumentedTypes() {
        JsonObject result = ResponseTruncation.Append(new { value = "x" }, 5000, true, 1000);

        using (Assert.EnterMultipleScope()) {
            Assert.That(result["value"]!.GetValue<string>(), Is.EqualTo("x"));
            Assert.That(result["truncated"]!.GetValueKind(), Is.EqualTo(JsonValueKind.True));
            Assert.That(result["original_bytes"]!.GetValue<long>(), Is.EqualTo(5000));
            Assert.That(result["original_bytes_exact"]!.GetValueKind(), Is.EqualTo(JsonValueKind.True));
            Assert.That(result["max_response_bytes"]!.GetValue<int>(), Is.EqualTo(1000));
        }
    }

    [Test]
    public void Append_Budget_UsesObservedBytesAndInexact() {
        ResponseBudget budget = new(100);
        budget.TryAdd(60);
        budget.TryAdd(70);

        JsonObject result = ResponseTruncation.Append(new { value = 1 }, budget);

        using (Assert.EnterMultipleScope()) {
            Assert.That(result["original_bytes"]!.GetValue<long>(), Is.EqualTo(130));
            Assert.That(result["original_bytes_exact"]!.GetValue<bool>(), Is.False);
            Assert.That(result["max_response_bytes"]!.GetValue<int>(), Is.EqualTo(100));
        }
    }

    [Test]
    public void Append_Body_UsesBodySizeInformation() {
        BoundedBody body = new(new byte[10], true, 7777, true);

        JsonObject result = ResponseTruncation.Append(new { value = 1 }, body, 1000);

        using (Assert.EnterMultipleScope()) {
            Assert.That(result["original_bytes"]!.GetValue<long>(), Is.EqualTo(7777));
            Assert.That(result["original_bytes_exact"]!.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public void Append_DataThatIsNotAnObject_Throws() {
        Assert.Throws<ArgumentException>(() => ResponseTruncation.Append(new[] { 1, 2 }, 5000, true, 1000));
    }

    [Test]
    public void ErrorData_ReturnsTruncationShape() {
        string json = JsonSerializer.Serialize(ResponseTruncation.ErrorData(5000, false, 1000));

        Assert.That(
            json,
            Is.EqualTo("""{"truncated":true,"original_bytes":5000,"original_bytes_exact":false,"max_response_bytes":1000}""")
        );
    }

    [Test]
    public void Message_MentionsSettingNameAndSizes() {
        string message = ResponseTruncation.Message(5000, true, 1000);

        Assert.That(message, Does.Contain("MCPLAB_MAX_RESPONSE_BYTES").And.Contain("5000").And.Contain("1000"));
    }

    [Test]
    public void Message_InexactSize_SaysAtLeast() {
        Assert.That(ResponseTruncation.Message(5000, false, 1000), Does.Contain("at least 5000"));
    }

    [Test]
    public void Append_ChineseData_KeepsFieldShapeAndOriginalCharacters() {
        JsonObject ascii = ResponseTruncation.Append(new { value = "abc" }, 5000, false, 1000);
        JsonObject chinese = ResponseTruncation.Append(new { value = "中文" }, 5000, false, 1000);

        string json = ToolResponse.Ok(chinese);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement data = document.RootElement.GetProperty("data");

        using (Assert.EnterMultipleScope()) {
            Assert.That(chinese.Select(property => property.Key), Is.EqualTo(ascii.Select(property => property.Key)));
            Assert.That(
                chinese.Select(property => property.Value!.GetValueKind()),
                Is.EqualTo(ascii.Select(property => property.Value!.GetValueKind()))
            );
            Assert.That(json, Does.Contain("中文").And.Not.Contain("\\u4E2D"));
            Assert.That(data.GetProperty("value").GetString(), Is.EqualTo("中文"));
            Assert.That(data.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("original_bytes").GetInt64(), Is.EqualTo(5000));
            Assert.That(data.GetProperty("original_bytes_exact").GetBoolean(), Is.False);
            Assert.That(data.GetProperty("max_response_bytes").GetInt32(), Is.EqualTo(1000));
        }
    }
}
