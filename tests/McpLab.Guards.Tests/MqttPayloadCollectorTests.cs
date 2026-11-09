using System.Text;
using System.Text.Json;
using CloudyWing.McpLab.Mosquitto;
using CloudyWing.McpLab.Shared;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class MqttPayloadCollectorTests {
    private const int OneMiB = 1024 * 1024;

    [Test]
    public void Add_PayloadFillsBudgetExactly_KeepsFullPayloadWithoutTruncation() {
        MqttPayloadCollector collector = new(new ResponseBudget(OneMiB));
        byte[] payload = Encoding.UTF8.GetBytes(new string('a', OneMiB));

        bool recorded = collector.Add("sensor/a", payload, 1);

        using (Assert.EnterMultipleScope()) {
            Assert.That(recorded, Is.True);
            Assert.That(collector.Truncated, Is.False);
            Assert.That(collector.ObservedBytes, Is.EqualTo(OneMiB));
            Assert.That(collector.Messages, Has.Count.EqualTo(1));
            Assert.That(
                JsonSerializer.Serialize(collector.Messages[0]),
                Does.StartWith("""{"topic":"sensor/a","payload":"aaa""").And.EndsWith("\",\"qos\":1}")
            );
            Assert.That(JsonSerializer.Serialize(collector.Messages[0]), Does.Not.Contain("payload_original_bytes"));
        }
    }

    [Test]
    public void Add_PayloadLargerThanBudget_KeepsPrefixAndRecordsOriginalSize() {
        MqttPayloadCollector collector = new(new ResponseBudget(OneMiB));
        byte[] payload = Encoding.UTF8.GetBytes(new string('b', 2 * OneMiB));

        collector.Add("sensor/b", payload, 0);

        JsonElement message = JsonSerializer.SerializeToElement(collector.Messages[0]);

        using (Assert.EnterMultipleScope()) {
            Assert.That(collector.Truncated, Is.True);
            Assert.That(collector.ObservedBytes, Is.EqualTo(2 * OneMiB));
            Assert.That(message.GetProperty("payload").GetString()!.Length, Is.EqualTo(OneMiB));
            Assert.That(message.GetProperty("payload_original_bytes").GetInt32(), Is.EqualTo(2 * OneMiB));
        }
    }

    [Test]
    public void Add_BudgetAlreadyUsed_IgnoresLaterMessageButCountsItsBytes() {
        MqttPayloadCollector collector = new(new ResponseBudget(10));
        collector.Add("t/1", Encoding.UTF8.GetBytes("0123456789"), 0);

        bool recorded = collector.Add("t/2", Encoding.UTF8.GetBytes("abcde"), 0);

        using (Assert.EnterMultipleScope()) {
            Assert.That(recorded, Is.False);
            Assert.That(collector.Messages, Has.Count.EqualTo(1));
            Assert.That(collector.Truncated, Is.True);
            Assert.That(collector.ObservedBytes, Is.EqualTo(15));
        }
    }

    [Test]
    public void Add_PayloadCutInsideMultiByteCharacter_DropsIncompleteSequence() {
        // "中中" is 6 bytes; a budget of 4 ends in the middle of the second character.
        MqttPayloadCollector collector = new(new ResponseBudget(4));

        collector.Add("t", Encoding.UTF8.GetBytes("中中"), 0);

        JsonElement message = JsonSerializer.SerializeToElement(collector.Messages[0]);

        using (Assert.EnterMultipleScope()) {
            Assert.That(message.GetProperty("payload").GetString(), Is.EqualTo("中"));
            Assert.That(message.GetProperty("payload_original_bytes").GetInt32(), Is.EqualTo(6));
            Assert.That(collector.Truncated, Is.True);
        }
    }

    [Test]
    public void Add_PayloadSegmentWithOffset_UsesOnlyTheSegmentBytes() {
        MqttPayloadCollector collector = new(new ResponseBudget(100));
        byte[] buffer = Encoding.UTF8.GetBytes("xxhelloxx");

        collector.Add("t", new ArraySegment<byte>(buffer, 2, 5), 0);

        JsonElement message = JsonSerializer.SerializeToElement(collector.Messages[0]);

        using (Assert.EnterMultipleScope()) {
            Assert.That(message.GetProperty("payload").GetString(), Is.EqualTo("hello"));
            Assert.That(collector.ObservedBytes, Is.EqualTo(5));
        }
    }

    [Test]
    public void Add_EmptyPayload_RecordsEmptyStringWithoutTruncation() {
        MqttPayloadCollector collector = new(new ResponseBudget(100));

        collector.Add("t", ArraySegment<byte>.Empty, 0);

        using (Assert.EnterMultipleScope()) {
            Assert.That(collector.Messages, Has.Count.EqualTo(1));
            Assert.That(collector.Truncated, Is.False);
            Assert.That(JsonSerializer.Serialize(collector.Messages[0]), Is.EqualTo("""{"topic":"t","payload":"","qos":0}"""));
        }
    }

    [Test]
    public void Add_ConcurrentCalls_DoesNotThrowAndKeepsPayloadWithinBudget() {
        const int Budget = 5000;
        MqttPayloadCollector collector = new(new ResponseBudget(Budget));
        byte[] payload = Encoding.UTF8.GetBytes(new string('c', 100));

        Assert.DoesNotThrow(() => Parallel.For(0, 200, i => collector.Add($"t/{i}", payload, 0)));

        using (Assert.EnterMultipleScope()) {
            Assert.That(collector.ObservedBytes, Is.EqualTo(200 * 100));
            Assert.That(collector.Messages, Has.Count.EqualTo(Budget / 100));
            Assert.That(collector.Truncated, Is.True);
        }
    }

    [Test]
    public void RecordStatus_TopicWithoutInterestKey_IsNotRetained() {
        Dictionary<string, string> stats = [];
        ResponseBudget budget = new(1000);

        bool kept = MqttTools.RecordStatus(
            stats,
            budget,
            "$SYS/broker/load/connections/1min",
            Encoding.UTF8.GetBytes("12.5")
        );

        using (Assert.EnterMultipleScope()) {
            Assert.That(kept, Is.False);
            Assert.That(stats, Is.Empty);
            Assert.That(budget.ObservedBytes, Is.EqualTo(0));
        }
    }

    [Test]
    public void RecordStatus_TopicWithInterestKey_IsRetainedAndLatestValueWins() {
        Dictionary<string, string> stats = [];
        ResponseBudget budget = new(1000);

        MqttTools.RecordStatus(stats, budget, "$SYS/broker/uptime", Encoding.UTF8.GetBytes("10 seconds"));
        MqttTools.RecordStatus(stats, budget, "$SYS/broker/uptime", Encoding.UTF8.GetBytes("12 seconds"));

        Assert.That(stats, Is.EqualTo(new Dictionary<string, string> { ["$SYS/broker/uptime"] = "12 seconds" }));
    }

    [Test]
    public void RecordStatus_LargePayloadOnInterestKey_IsCutToBudget() {
        Dictionary<string, string> stats = [];
        ResponseBudget budget = new(8);

        MqttTools.RecordStatus(stats, budget, "$SYS/broker/version", Encoding.UTF8.GetBytes("mosquitto version 2.0.18"));

        Assert.That(stats["$SYS/broker/version"], Is.EqualTo("mosquitt"));
    }

    [Test]
    public void CreateResponseData_NotTruncated_IsArrayWithoutNewFields() {
        MqttPayloadCollector collector = new(new ResponseBudget(100));
        collector.Add("t/1", Encoding.UTF8.GetBytes("one"), 0);
        collector.Add("t/2", Encoding.UTF8.GetBytes("two"), 1);

        string json = JsonSerializer.Serialize(collector.CreateResponseData(100));

        Assert.That(
            json,
            Is.EqualTo("""[{"topic":"t/1","payload":"one","qos":0},{"topic":"t/2","payload":"two","qos":1}]""")
        );
    }

    [Test]
    public void CreateResponseData_LastPayloadCut_KeepsArrayAndAddsFieldsToLastMessageOnly() {
        MqttPayloadCollector collector = new(new ResponseBudget(8));
        collector.Add("t/1", Encoding.UTF8.GetBytes("abc"), 0);
        collector.Add("t/2", Encoding.UTF8.GetBytes("0123456789"), 0);

        JsonElement data = JsonSerializer.SerializeToElement(collector.CreateResponseData(8));
        JsonElement first = data[0];
        JsonElement last = data[1];

        using (Assert.EnterMultipleScope()) {
            Assert.That(data.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(data.GetArrayLength(), Is.EqualTo(2));
            Assert.That(first.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "topic", "payload", "qos" }));
            Assert.That(last.GetProperty("payload").GetString(), Is.EqualTo("01234"));
            Assert.That(last.GetProperty("payload_original_bytes").GetInt32(), Is.EqualTo(10));
            Assert.That(last.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(last.GetProperty("original_bytes").GetInt64(), Is.EqualTo(13));
            Assert.That(last.GetProperty("original_bytes_exact").GetBoolean(), Is.False);
            Assert.That(last.GetProperty("max_response_bytes").GetInt32(), Is.EqualTo(8));
        }
    }

    [Test]
    public void CreateResponseData_LaterMessageIgnored_AddsFieldsToLastRecordedMessage() {
        MqttPayloadCollector collector = new(new ResponseBudget(10));
        collector.Add("t/1", Encoding.UTF8.GetBytes("0123456789"), 0);
        collector.Add("t/2", Encoding.UTF8.GetBytes("abcde"), 0);

        JsonElement data = JsonSerializer.SerializeToElement(collector.CreateResponseData(10));
        JsonElement last = data[0];

        using (Assert.EnterMultipleScope()) {
            Assert.That(data.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(data.GetArrayLength(), Is.EqualTo(1));
            Assert.That(last.TryGetProperty("payload_original_bytes", out _), Is.False);
            Assert.That(last.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(last.GetProperty("original_bytes").GetInt64(), Is.EqualTo(15));
            Assert.That(last.GetProperty("original_bytes_exact").GetBoolean(), Is.False);
            Assert.That(last.GetProperty("max_response_bytes").GetInt32(), Is.EqualTo(10));
        }
    }
}
