using NUnit.Framework;

using ElasticGuard = CloudyWing.McpLab.Elastic.ElasticGuard;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ElasticGuardTests {
    [Test]
    public void ValidateQueryBody_FilteredQuery_DoesNotThrow() {
        const string queryJson = """
        {
          "query": {
            "term": {
              "status": "active"
            }
          }
        }
        """;

        Assert.DoesNotThrow(() => ElasticGuard.ValidateQueryBody(queryJson, "delete"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ValidateQueryBody_EmptyQuery_ThrowsInvalidOperationException(string queryJson) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => ElasticGuard.ValidateQueryBody(queryJson, "delete"))!;

        Assert.That(exception.Message, Does.Contain("requires a non-empty query"));
    }

    [Test]
    public void ValidateQueryBody_InvalidJson_ThrowsInvalidOperationException() {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => ElasticGuard.ValidateQueryBody("{", "delete"))!;

        Assert.That(exception.Message, Does.Contain("Invalid JSON query body"));
    }

    [TestCase("""{"match_all":{}}""")]
    [TestCase("""{"query":{"match_all":{}}}""")]
    [TestCase("""{"query":{"bool":{"must":{"match_all":{}}}}}""")]
    [TestCase("""{"query":{"bool":{"should":[{"term":{"a":1}},{"match_all":{"boost":1}}]}}}""")]
    public void ValidateQueryBody_MatchAllQuery_ThrowsInvalidOperationException(string queryJson) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => ElasticGuard.ValidateQueryBody(queryJson, "delete"))!;

        Assert.That(exception.Message, Does.Contain("match_all"));
    }

    [TestCase("{}")]
    [TestCase("""{"query":{}}""")]
    [TestCase("""{"size":10}""")]
    public void ValidateQueryBody_MissingOrEmptyQueryObject_ThrowsInvalidOperationException(string queryJson) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => ElasticGuard.ValidateQueryBody(queryJson, "delete"))!;

        Assert.That(exception.Message, Does.Contain("non-empty \"query\" object"));
    }

    [TestCase("[]")]
    [TestCase("\"text\"")]
    public void ValidateQueryBody_NonObjectJson_ThrowsInvalidOperationException(string queryJson) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => ElasticGuard.ValidateQueryBody(queryJson, "delete"))!;

        Assert.That(exception.Message, Does.Contain("JSON object"));
    }

    [TestCase("logs-2026.11")]
    [TestCase("orders")]
    public void ValidateSingleIndex_ConcreteIndex_DoesNotThrow(string index) {
        Assert.DoesNotThrow(() => ElasticGuard.ValidateSingleIndex(index, "delete"));
    }

    [TestCase("")]
    [TestCase("*")]
    [TestCase("logs-*")]
    [TestCase("orders,users")]
    [TestCase("_all")]
    [TestCase("-orders")]
    public void ValidateSingleIndex_MultiIndexTarget_ThrowsInvalidOperationException(string index) {
        Assert.Throws<InvalidOperationException>(() => ElasticGuard.ValidateSingleIndex(index, "delete"));
    }
}
