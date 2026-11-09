using System.Text;

using CloudyWing.McpLab.Redis;
using CloudyWing.McpLab.Shared;

using NSubstitute;

using NUnit.Framework;

using StackExchange.Redis;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class RedisValueReaderTests {
    private const string Key = "sample";

    [Test]
    public async Task ReadStringAsync_ValueLargerThanLimit_ReadsOnlyPrefixAndReportsStrlen() {
        const long strlen = 20L * 1024 * 1024;
        const int maxBytes = 1024;
        IDatabase db = Substitute.For<IDatabase>();
        db.StringLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(strlen);
        db.StringGetRangeAsync(Key, 0, maxBytes - 1, Arg.Any<CommandFlags>())
            .Returns((RedisValue)new string('a', maxBytes));

        (string value, long originalBytes, bool truncated) = await RedisValueReader.ReadStringAsync(db, Key, maxBytes);

        Assert.Multiple(() => {
            Assert.That(value, Has.Length.EqualTo(maxBytes));
            Assert.That(originalBytes, Is.EqualTo(strlen));
            Assert.That(truncated, Is.True);
        });
        await db.DidNotReceive().StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
        await db.Received(1).StringGetRangeAsync(Key, 0, maxBytes - 1, Arg.Any<CommandFlags>());
    }

    [Test]
    public async Task ReadStringAsync_PrefixEndsInsideMultiByteCharacter_DropsIncompleteSequence() {
        byte[] bytes = Encoding.UTF8.GetBytes("ab測試");
        int maxBytes = bytes.Length - 1;
        IDatabase db = Substitute.For<IDatabase>();
        db.StringLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(bytes.Length);
        db.StringGetRangeAsync(Key, 0, maxBytes - 1, Arg.Any<CommandFlags>())
            .Returns((RedisValue)bytes[..maxBytes]);

        (string value, _, bool truncated) = await RedisValueReader.ReadStringAsync(db, Key, maxBytes);

        Assert.Multiple(() => {
            Assert.That(value, Is.EqualTo("ab測"));
            Assert.That(truncated, Is.True);
        });
    }

    [TestCase(10, 10)]
    [TestCase(10, 100)]
    public async Task ReadStringAsync_ValueWithinLimit_ReadsWholeValue(int length, int maxBytes) {
        IDatabase db = Substitute.For<IDatabase>();
        db.StringLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(length);
        db.StringGetAsync(Key, Arg.Any<CommandFlags>()).Returns((RedisValue)new string('b', length));

        (string value, long originalBytes, bool truncated) = await RedisValueReader.ReadStringAsync(db, Key, maxBytes);

        Assert.Multiple(() => {
            Assert.That(value, Is.EqualTo(new string('b', length)));
            Assert.That(originalBytes, Is.EqualTo(length));
            Assert.That(truncated, Is.False);
        });
        await db.DidNotReceive().StringGetRangeAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>());
    }

    [Test]
    public async Task ReadStringAsync_ValueIsNull_KeepsPreviousTextConversion() {
        IDatabase db = Substitute.For<IDatabase>();
        db.StringLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(0L);
        db.StringGetAsync(Key, Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

        (string value, _, bool truncated) = await RedisValueReader.ReadStringAsync(db, Key, 100);

        Assert.Multiple(() => {
            Assert.That(value, Is.EqualTo(RedisValue.Null.ToString() ?? "(nil)"));
            Assert.That(truncated, Is.False);
        });
    }

    [Test]
    public async Task ReadListAsync_BudgetSmallerThanList_StopsEarlyAndMarksBudgetExhausted() {
        IDatabase db = Substitute.For<IDatabase>();
        db.ListRangeAsync(Key, 0, 9, Arg.Any<CommandFlags>()).Returns(Values(10, "abcde"));
        db.ListLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(10L);
        ResponseBudget budget = new(12);

        (object value, long total, int returned) = await RedisValueReader.ReadListAsync(db, Key, 10, budget);

        Assert.Multiple(() => {
            Assert.That(((IReadOnlyList<string>)value), Has.Count.EqualTo(2));
            Assert.That(total, Is.EqualTo(10));
            Assert.That(returned, Is.EqualTo(2));
            Assert.That(budget.Exhausted, Is.True);
            Assert.That(budget.ObservedBytes, Is.EqualTo(15));
        });
    }

    [Test]
    public async Task ReadListAsync_ItemLimitReachedBeforeBudget_LimitsByItemCountOnly() {
        IDatabase db = Substitute.For<IDatabase>();
        db.ListRangeAsync(Key, 0, 2, Arg.Any<CommandFlags>()).Returns(Values(3, "abcde"));
        db.ListLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(50L);
        ResponseBudget budget = new(1000);

        (object value, long total, int returned) = await RedisValueReader.ReadListAsync(db, Key, 3, budget);

        Assert.Multiple(() => {
            Assert.That(((IReadOnlyList<string>)value), Has.Count.EqualTo(3));
            Assert.That(total, Is.EqualTo(50));
            Assert.That(returned, Is.EqualTo(3));
            Assert.That(budget.Exhausted, Is.False);
        });
    }

    [Test]
    public async Task ReadListAsync_BudgetExhaustedInFirstChunk_DoesNotFetchFurtherChunks() {
        IDatabase db = Substitute.For<IDatabase>();
        db.ListRangeAsync(Key, 0, 99, Arg.Any<CommandFlags>()).Returns(Values(100, "abcde"));
        db.ListRangeAsync(Key, 100, 199, Arg.Any<CommandFlags>()).Returns(Values(100, "abcde"));
        db.ListLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(1000L);
        ResponseBudget budget = new(20);

        (_, _, int returned) = await RedisValueReader.ReadListAsync(db, Key, 1000, budget);

        Assert.That(returned, Is.EqualTo(4));
        await db.DidNotReceive().ListRangeAsync(Key, 100, 199, Arg.Any<CommandFlags>());
    }

    [Test]
    public async Task ReadListAsync_ItemLimitSpansChunks_ReadsAllRequestedItems() {
        IDatabase db = Substitute.For<IDatabase>();
        db.ListRangeAsync(Key, 0, 99, Arg.Any<CommandFlags>()).Returns(Values(100, "a"));
        db.ListRangeAsync(Key, 100, 149, Arg.Any<CommandFlags>()).Returns(Values(50, "a"));
        db.ListLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(150L);

        (_, long total, int returned) = await RedisValueReader.ReadListAsync(db, Key, 150, new ResponseBudget(1000));

        Assert.Multiple(() => {
            Assert.That(returned, Is.EqualTo(150));
            Assert.That(total, Is.EqualTo(150));
        });
    }

    [Test]
    public async Task ReadSetAsync_BudgetSmallerThanSet_StopsEnumerationEarly() {
        IDatabase db = Substitute.For<IDatabase>();
        int enumerated = 0;
        db.SetScanAsync(Key, Arg.Any<RedisValue>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(_ => Scan(100, "abcde", () => enumerated++));
        db.SetLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(100L);
        ResponseBudget budget = new(12);

        (object value, long total, int returned) = await RedisValueReader.ReadSetAsync(db, Key, 100, budget);

        Assert.Multiple(() => {
            Assert.That(((IReadOnlyList<string>)value), Has.Count.EqualTo(2));
            Assert.That(total, Is.EqualTo(100));
            Assert.That(returned, Is.EqualTo(2));
            Assert.That(enumerated, Is.EqualTo(3));
            Assert.That(budget.Exhausted, Is.True);
        });
    }

    [Test]
    public async Task ReadHashAsync_BudgetSmallerThanHash_StopsEnumerationEarly() {
        IDatabase db = Substitute.For<IDatabase>();
        int enumerated = 0;
        db.HashScanAsync(Key, Arg.Any<RedisValue>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(_ => ScanHash(100, () => enumerated++));
        db.HashLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(100L);
        ResponseBudget budget = new(25);

        (object value, long total, int returned) = await RedisValueReader.ReadHashAsync(db, Key, 100, budget);

        // Each field costs "f0" + "value" = 7 bytes, so three fields fit in 25 bytes.
        Assert.Multiple(() => {
            Assert.That(((IReadOnlyDictionary<string, string>)value), Has.Count.EqualTo(3));
            Assert.That(total, Is.EqualTo(100));
            Assert.That(returned, Is.EqualTo(3));
            Assert.That(enumerated, Is.EqualTo(4));
            Assert.That(budget.Exhausted, Is.True);
        });
    }

    [Test]
    public async Task ReadHashAsync_ItemLimitReachedBeforeBudget_LimitsByItemCountOnly() {
        IDatabase db = Substitute.For<IDatabase>();
        db.HashScanAsync(Key, Arg.Any<RedisValue>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(_ => ScanHash(100, () => { }));
        db.HashLengthAsync(Key, Arg.Any<CommandFlags>()).Returns(100L);
        ResponseBudget budget = new(10_000);

        (_, _, int returned) = await RedisValueReader.ReadHashAsync(db, Key, 5, budget);

        Assert.Multiple(() => {
            Assert.That(returned, Is.EqualTo(5));
            Assert.That(budget.Exhausted, Is.False);
        });
    }

    [Test]
    public async Task ReadSortedSetAsync_BudgetSmallerThanSet_StopsEarly() {
        IDatabase db = Substitute.For<IDatabase>();
        SortedSetEntry[] entries = Enumerable.Range(0, 10).Select(i => new SortedSetEntry("member", i)).ToArray();
        db.SortedSetRangeByRankWithScoresAsync(Key, 0, 9, Arg.Any<Order>(), Arg.Any<CommandFlags>()).Returns(entries);
        db.SortedSetLengthAsync(Key, Arg.Any<double>(), Arg.Any<double>(), Arg.Any<Exclude>(), Arg.Any<CommandFlags>()).Returns(10L);
        // {"member":"member","score":0} is 29 bytes.
        ResponseBudget budget = new(60);

        (object value, long total, int returned) = await RedisValueReader.ReadSortedSetAsync(db, Key, 10, budget);

        Assert.Multiple(() => {
            Assert.That(((System.Collections.ICollection)value), Has.Count.EqualTo(2));
            Assert.That(total, Is.EqualTo(10));
            Assert.That(returned, Is.EqualTo(2));
            Assert.That(budget.Exhausted, Is.True);
        });
    }

    [Test]
    public async Task ReadCollectionAsync_UnknownType_ReturnsExplanatoryTextWithoutReading() {
        IDatabase db = Substitute.For<IDatabase>();

        (object value, long total, int returned) = await RedisValueReader.ReadCollectionAsync(
            db, RedisType.Stream, Key, 10, new ResponseBudget(100)
        );

        Assert.Multiple(() => {
            Assert.That(value, Is.EqualTo("Unknown type: Stream"));
            Assert.That(total, Is.Zero);
            Assert.That(returned, Is.Zero);
        });
    }

    private static Task<RedisValue[]> Values(int count, string value) =>
        Task.FromResult(Enumerable.Range(0, count).Select(_ => (RedisValue)value).ToArray());

    private static async IAsyncEnumerable<RedisValue> Scan(int count, string value, Action onYield) {
        for (int i = 0; i < count; i++) {
            onYield();
            yield return value;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<HashEntry> ScanHash(int count, Action onYield) {
        for (int i = 0; i < count; i++) {
            onYield();
            yield return new HashEntry($"f{i}", "value");
        }

        await Task.CompletedTask;
    }
}
