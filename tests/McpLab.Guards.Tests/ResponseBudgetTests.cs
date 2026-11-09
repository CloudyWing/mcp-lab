using System.Text;
using CloudyWing.McpLab.Shared;

using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ResponseBudgetTests {
    [Test]
    public void TryAdd_FitsInRemaining_AcceptsAndTracksUsage() {
        ResponseBudget budget = new(100);

        bool accepted = budget.TryAdd(60);

        using (Assert.EnterMultipleScope()) {
            Assert.That(accepted, Is.True);
            Assert.That(budget.UsedBytes, Is.EqualTo(60));
            Assert.That(budget.ObservedBytes, Is.EqualTo(60));
            Assert.That(budget.Remaining, Is.EqualTo(40));
            Assert.That(budget.Exhausted, Is.False);
        }
    }

    [Test]
    public void TryAdd_DoesNotFit_RejectsWholeItemAndMarksExhausted() {
        ResponseBudget budget = new(100);
        budget.TryAdd(60);

        bool accepted = budget.TryAdd(50);

        using (Assert.EnterMultipleScope()) {
            Assert.That(accepted, Is.False);
            Assert.That(budget.UsedBytes, Is.EqualTo(60));
            Assert.That(budget.ObservedBytes, Is.EqualTo(110));
            Assert.That(budget.Exhausted, Is.True);
        }
    }

    [Test]
    public void TryAdd_ExactlyMaxBytes_Accepts() {
        ResponseBudget budget = new(100);

        bool accepted = budget.TryAdd(100);

        using (Assert.EnterMultipleScope()) {
            Assert.That(accepted, Is.True);
            Assert.That(budget.Remaining, Is.Zero);
            Assert.That(budget.Exhausted, Is.False);
        }
    }

    [Test]
    public void TakeUpTo_MoreThanRemaining_AcceptsOnlyRemainingAndMarksExhausted() {
        ResponseBudget budget = new(100);
        budget.TakeUpTo(70);

        long accepted = budget.TakeUpTo(50);

        using (Assert.EnterMultipleScope()) {
            Assert.That(accepted, Is.EqualTo(30));
            Assert.That(budget.UsedBytes, Is.EqualTo(100));
            Assert.That(budget.ObservedBytes, Is.EqualTo(120));
            Assert.That(budget.Exhausted, Is.True);
        }
    }

    [Test]
    public void TakeUpTo_ExactlyRemaining_DoesNotMarkExhausted() {
        ResponseBudget budget = new(100);

        long accepted = budget.TakeUpTo(100);

        using (Assert.EnterMultipleScope()) {
            Assert.That(accepted, Is.EqualTo(100));
            Assert.That(budget.Exhausted, Is.False);
        }
    }

    [Test]
    public void TryAddJson_UsesUtf8JsonLength() {
        ResponseBudget budget = new(100);

        bool accepted = budget.TryAddJson(new { a = "xyz", n = 12 });

        using (Assert.EnterMultipleScope()) {
            Assert.That(accepted, Is.True);
            Assert.That(budget.UsedBytes, Is.EqualTo("""{"a":"xyz","n":12}"""u8.Length));
        }
    }

    [Test]
    public void TryAddJson_LargerThanRemaining_Rejects() {
        ResponseBudget budget = new(5);

        bool accepted = budget.TryAddJson(new { value = "abcdef" });

        using (Assert.EnterMultipleScope()) {
            Assert.That(accepted, Is.False);
            Assert.That(budget.UsedBytes, Is.Zero);
            Assert.That(budget.Exhausted, Is.True);
        }
    }

    [TestCase(null, 0)]
    [TestCase("", 0)]
    [TestCase("abc", 3)]
    [TestCase("中文", 6)]
    public void Utf8Size_ReturnsUtf8ByteCount(string? text, int expected) {
        Assert.That(ResponseBudget.Utf8Size(text), Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Constructor_NonPositiveMaxBytes_Throws(int maxBytes) {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResponseBudget(maxBytes));
    }

    [Test]
    public void TryAddJson_CjkItem_MeasuresUnescapedUtf8Bytes() {
        int expected = Encoding.UTF8.GetByteCount("{\"a\":\"中文\"}");
        ResponseBudget exact = new(expected);
        ResponseBudget oneShort = new(expected - 1);

        bool exactAccepted = exact.TryAddJson(new { a = "中文" });
        bool oneShortAccepted = oneShort.TryAddJson(new { a = "中文" });

        using (Assert.EnterMultipleScope()) {
            Assert.That(exactAccepted, Is.True);
            Assert.That(exact.UsedBytes, Is.EqualTo(expected));
            Assert.That(oneShortAccepted, Is.False);
            Assert.That(oneShort.UsedBytes, Is.Zero);
        }
    }
}
