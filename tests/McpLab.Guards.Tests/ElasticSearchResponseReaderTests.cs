using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CloudyWing.McpLab.Elastic;
using CloudyWing.McpLab.Shared;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ElasticSearchResponseReaderTests {
    private const string FullResponse =
        """{"took":3,"timed_out":false,"_shards":{"total":1,"successful":1},"hits":{"total":{"value":50,"relation":"eq"},"max_score":1.5,"hits":[{"_index":"i","_id":"1","_score":1.5,"_source":{"name":"alpha","tags":["x","y"]}},{"_index":"i","_id":"2","_score":1.2,"_source":{"name":"beta"}}]}}""";

    [Test]
    public void Read_CompleteBody_ReturnsTotalAndHitsWithoutTruncationFields() {
        BoundedBody body = Complete(FullResponse);

        JsonNode? json = ToNode(ElasticSearchResponseReader.Read(body, 1000));

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                json!.ToJsonString(),
                Is.EqualTo("""{"total":"50","hits":[{"_id":"1","_score":"1.5","_source":{"name":"alpha","tags":["x","y"]}},{"_id":"2","_score":"1.2","_source":{"name":"beta"}}]}""")
            );
            Assert.That(json["original_bytes"], Is.Null);
            Assert.That(json["truncated"], Is.Null);
        }
    }

    [Test]
    public void Read_CompleteBodyWithoutHits_ReturnsZeroTotalAndEmptyHits() {
        BoundedBody body = Complete("""{"took":1,"hits":{"hits":[]}}""");

        JsonNode? json = ToNode(ElasticSearchResponseReader.Read(body, 1000));

        Assert.That(json!.ToJsonString(), Is.EqualTo("""{"total":"0","hits":[]}"""));
    }

    [Test]
    public void Read_TruncatedInsideSecondHit_ReturnsOnlyCompleteHit() {
        int cut = FullResponse.IndexOf("\"beta\"", StringComparison.Ordinal);
        BoundedBody body = Truncated(FullResponse, cut);

        JsonNode? json = ToNode(ElasticSearchResponseReader.Read(body, 500));

        using (Assert.EnterMultipleScope()) {
            Assert.That(json!["total"]!.GetValue<string>(), Is.EqualTo("50"));
            Assert.That(json["hits"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(json["hits"]![0]!["_id"]!.GetValue<string>(), Is.EqualTo("1"));
            Assert.That(json["hits"]![0]!["_source"]!["tags"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(json["truncated"]!.GetValue<bool>(), Is.True);
            Assert.That(json["original_bytes"]!.GetValue<long>(), Is.EqualTo(Encoding.UTF8.GetByteCount(FullResponse)));
            Assert.That(json["original_bytes_exact"]!.GetValue<bool>(), Is.True);
            Assert.That(json["max_response_bytes"]!.GetValue<int>(), Is.EqualTo(500));
        }
    }

    [Test]
    public void Read_TruncatedBetweenHits_ReturnsAllCompleteHits() {
        int cut = FullResponse.LastIndexOf("]}}", StringComparison.Ordinal);
        BoundedBody body = Truncated(FullResponse, cut);

        JsonNode? json = ToNode(ElasticSearchResponseReader.Read(body, 500));

        using (Assert.EnterMultipleScope()) {
            Assert.That(json!["hits"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(json["truncated"]!.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public void Read_TruncatedAfterFirstHitComma_ReturnsFirstHit() {
        int cut = FullResponse.IndexOf(",{\"_index\":\"i\",\"_id\":\"2\"", StringComparison.Ordinal) + 1;
        BoundedBody body = Truncated(FullResponse, cut);

        JsonNode? json = ToNode(ElasticSearchResponseReader.Read(body, 500));

        using (Assert.EnterMultipleScope()) {
            Assert.That(json!["hits"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(json["hits"]![0]!["_source"]!["name"]!.GetValue<string>(), Is.EqualTo("alpha"));
        }
    }

    [Test]
    public void Read_TruncatedBeforeTotal_ReturnsEmptyHits() {
        BoundedBody body = Truncated(FullResponse, FullResponse.IndexOf("\"total\":{\"value\"", StringComparison.Ordinal) + 8);

        JsonNode? json = ToNode(ElasticSearchResponseReader.Read(body, 500));

        using (Assert.EnterMultipleScope()) {
            Assert.That(json!["total"]!.GetValue<string>(), Is.EqualTo("0"));
            Assert.That(json["hits"]!.AsArray(), Is.Empty);
            Assert.That(json["truncated"]!.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public void Read_TruncatedAfterTotalBeforeFirstHit_ReturnsTotalAndEmptyHits() {
        BoundedBody body = Truncated(FullResponse, FullResponse.IndexOf("\"_id\":\"1\"", StringComparison.Ordinal));

        JsonNode? json = ToNode(ElasticSearchResponseReader.Read(body, 500));

        using (Assert.EnterMultipleScope()) {
            Assert.That(json!["total"]!.GetValue<string>(), Is.EqualTo("50"));
            Assert.That(json["hits"]!.AsArray(), Is.Empty);
        }
    }

    [Test]
    public void Read_TruncatedNonJsonPrefix_DoesNotThrowAndReturnsEmptyHits() {
        BoundedBody body = Truncated("<html><body>Bad Gateway, this is not JSON at all</body></html>", 30);

        JsonNode? json = ToNode(ElasticSearchResponseReader.Read(body, 500));

        using (Assert.EnterMultipleScope()) {
            Assert.That(json!["hits"]!.AsArray(), Is.Empty);
            Assert.That(json["truncated"]!.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public void Read_TruncatedUnknownSize_ReportsInexactOriginalBytes() {
        byte[] prefix = Encoding.UTF8.GetBytes(FullResponse[..40]);
        BoundedBody body = new(prefix, true, 501, false);

        JsonNode? json = ToNode(ElasticSearchResponseReader.Read(body, 500));

        using (Assert.EnterMultipleScope()) {
            Assert.That(json!["original_bytes"]!.GetValue<long>(), Is.EqualTo(501));
            Assert.That(json["original_bytes_exact"]!.GetValue<bool>(), Is.False);
        }
    }

    private static BoundedBody Complete(string text) {
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        return new BoundedBody(bytes, false, bytes.Length, true);
    }

    private static BoundedBody Truncated(string text, int cutAtCharIndex) {
        byte[] all = Encoding.UTF8.GetBytes(text);
        byte[] prefix = Encoding.UTF8.GetBytes(text[..cutAtCharIndex]);

        return new BoundedBody(prefix, true, all.Length, true);
    }

    private static JsonNode? ToNode(object data) => JsonSerializer.SerializeToNode(data);
}
