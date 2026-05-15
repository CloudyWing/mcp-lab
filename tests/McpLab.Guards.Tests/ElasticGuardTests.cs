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
    public void ValidateQueryBody_MatchAllQuery_ThrowsInvalidOperationException(string queryJson) {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => ElasticGuard.ValidateQueryBody(queryJson, "delete"))!;

        Assert.That(exception.Message, Does.Contain("match_all"));
    }
}
