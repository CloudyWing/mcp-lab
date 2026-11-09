namespace CloudyWing.McpLab.Redis;

/// <summary>
/// Reads Redis values while keeping the amount of returned content within a <see cref="ResponseBudget"/>.
/// </summary>
public static class RedisValueReader {
    // Collections are fetched in chunks so that an exhausted budget stops the reading before the whole range is loaded.
    private const int ChunkSize = 100;

    /// <summary>
    /// Reads a string value. A value larger than <paramref name="maxBytes"/> is read as a prefix with GETRANGE,
    /// so that the whole value is never loaded.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="key">The key of the string value.</param>
    /// <param name="maxBytes">Largest number of value bytes to return.</param>
    /// <returns>The text, the size in bytes reported by STRLEN, and whether the text is a prefix of the value.</returns>
    public static async Task<(string Value, long OriginalBytes, bool Truncated)> ReadStringAsync(
        IDatabase db,
        string key,
        int maxBytes
    ) {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        long length = await db.StringLengthAsync(key);

        if (length > maxBytes) {
            RedisValue range = await db.StringGetRangeAsync(key, 0, maxBytes - 1);
            byte[] prefix = (byte[]?)range ?? [];

            return (Utf8Text.DecodePrefix(prefix, true), length, true);
        }

        // The value may change between STRLEN and GET, so the content that is actually read is returned as is.
        RedisValue value = await db.StringGetAsync(key);

        return (value.ToString() ?? "(nil)", length, false);
    }

    /// <summary>
    /// Reads a collection value of the given type within <paramref name="budget"/>.
    /// A type that is not a collection returns an explanatory text with no items.
    /// </summary>
    public static async Task<(object Value, long Total, int Returned)> ReadCollectionAsync(
        IDatabase db,
        RedisType type,
        string key,
        int max,
        ResponseBudget budget
    ) {
        return type switch {
            RedisType.List => await ReadListAsync(db, key, max, budget),
            RedisType.Set => await ReadSetAsync(db, key, max, budget),
            RedisType.SortedSet => await ReadSortedSetAsync(db, key, max, budget),
            RedisType.Hash => await ReadHashAsync(db, key, max, budget),
            _ => ((object)$"Unknown type: {type}", 0L, 0),
        };
    }

    /// <summary>
    /// Reads the first <paramref name="max"/> items of a list, stopping when <paramref name="budget"/> is exhausted.
    /// </summary>
    public static async Task<(object Value, long Total, int Returned)> ReadListAsync(
        IDatabase db,
        string key,
        int max,
        ResponseBudget budget
    ) {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(budget);

        List<string> items = [];

        for (long start = 0; start < max && !budget.Exhausted; start += ChunkSize) {
            long end = Math.Min(start + ChunkSize, max) - 1;
            RedisValue[] chunk = await db.ListRangeAsync(key, start, end);

            foreach (RedisValue item in chunk) {
                string text = item.ToString();

                if (!budget.TryAdd(ResponseBudget.Utf8Size(text))) {
                    break;
                }

                items.Add(text);
            }

            if (chunk.Length < end - start + 1) {
                break;
            }
        }

        long total = await db.ListLengthAsync(key);

        return (items, total, items.Count);
    }

    /// <summary>
    /// Reads at most <paramref name="max"/> members of a set, stopping when <paramref name="budget"/> is exhausted.
    /// </summary>
    public static async Task<(object Value, long Total, int Returned)> ReadSetAsync(
        IDatabase db,
        string key,
        int max,
        ResponseBudget budget
    ) {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(budget);

        List<string> items = [];

        await foreach (RedisValue member in db.SetScanAsync(key, pageSize: Math.Min(max, ChunkSize))) {
            string text = member.ToString();

            if (!budget.TryAdd(ResponseBudget.Utf8Size(text))) {
                break;
            }

            items.Add(text);

            if (items.Count >= max) {
                break;
            }
        }

        long total = await db.SetLengthAsync(key);

        return (items, total, items.Count);
    }

    /// <summary>
    /// Reads the first <paramref name="max"/> members of a sorted set with their scores,
    /// stopping when <paramref name="budget"/> is exhausted.
    /// </summary>
    public static async Task<(object Value, long Total, int Returned)> ReadSortedSetAsync(
        IDatabase db,
        string key,
        int max,
        ResponseBudget budget
    ) {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(budget);

        List<object> items = [];

        for (long start = 0; start < max && !budget.Exhausted; start += ChunkSize) {
            long end = Math.Min(start + ChunkSize, max) - 1;
            SortedSetEntry[] chunk = await db.SortedSetRangeByRankWithScoresAsync(key, start, end);

            foreach (SortedSetEntry entry in chunk) {
                var item = new { member = entry.Element.ToString(), score = entry.Score };

                if (!budget.TryAddJson(item)) {
                    break;
                }

                items.Add(item);
            }

            if (chunk.Length < end - start + 1) {
                break;
            }
        }

        long total = await db.SortedSetLengthAsync(key);

        return (items, total, items.Count);
    }

    /// <summary>
    /// Reads at most <paramref name="max"/> fields of a hash, stopping when <paramref name="budget"/> is exhausted.
    /// </summary>
    public static async Task<(object Value, long Total, int Returned)> ReadHashAsync(
        IDatabase db,
        string key,
        int max,
        ResponseBudget budget
    ) {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(budget);

        Dictionary<string, string> items = [];

        await foreach (HashEntry entry in db.HashScanAsync(key, pageSize: Math.Min(max, ChunkSize))) {
            string name = entry.Name.ToString();
            string value = entry.Value.ToString();

            if (!budget.TryAdd((long)ResponseBudget.Utf8Size(name) + ResponseBudget.Utf8Size(value))) {
                break;
            }

            items[name] = value;

            if (items.Count >= max) {
                break;
            }
        }

        long total = await db.HashLengthAsync(key);

        return (items, total, items.Count);
    }
}
