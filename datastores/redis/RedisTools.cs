namespace CloudyWing.McpLab.Redis;

/// <summary>
/// Provides MCP tools for reading from and writing to Redis instances.
/// </summary>
[McpServerToolType]
public sealed class RedisTools {
    private static readonly JsonSerializerOptions JsonCompact = new() { WriteIndented = false };
    private readonly ConnectionRegistry registry;

    /// <summary>
    /// Initializes a new instance of <see cref="RedisTools"/> with the specified connection registry.
    /// </summary>
    public RedisTools(ConnectionRegistry registry) {
        this.registry = registry;
    }

    /// <summary>
    /// 列出所有已設定的 Redis 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 Redis 連線")]
    public string ListConnections() =>
        JsonSerializer.Serialize(
            registry.All.Select(kv => new {
                name = kv.Key,
                host = kv.Value.Host,
                port = kv.Value.Port,
                database = kv.Value.Database,
            }), JsonCompact
        );

    /// <summary>
    /// 測試 Redis 連線是否正常（PING）
    /// </summary>
    [McpServerTool, Description("測試 Redis 連線是否正常（PING）")]
    public async Task<string> PingConnection(
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            using IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            TimeSpan latency = await db.PingAsync();

            return $"OK: {latency.TotalMilliseconds:F1}ms";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 列出符合 pattern 的 keys（預設最多 100 個）
    /// </summary>
    [McpServerTool, Description("列出符合 pattern 的 keys（預設最多 100 個）")]
    public async Task<string> ListKeys(
        [Description("Glob 模式，預設 *")] string pattern = "*",
        [Description("最多回傳筆數")] int count = 100,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            IServer server = mux.GetServers().First();
            List<string> keys = [];

            await foreach (RedisKey key in server.KeysAsync(pattern: pattern, pageSize: count)) {
                keys.Add(key.ToString());

                if (keys.Count >= count) {
                    break;
                }
            }

            return keys.Count > 0 ? string.Join("\n", keys) : "No keys found.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 取得指定 key 的值（自動偵測型別）
    /// </summary>
    [McpServerTool, Description("取得指定 key 的值（自動偵測型別）")]
    public async Task<string> GetKey(
        [Description("Redis key 名稱")] string key,
        [Description("連線名稱")] string connection = "") {
        try {
            using IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            RedisType type = await db.KeyTypeAsync(key);

            string result = type switch {
                RedisType.String =>
                    (await db.StringGetAsync(key)).ToString() ?? "(nil)",
                RedisType.List =>
                    JsonSerializer.Serialize((await db.ListRangeAsync(key)).Select(v => v.ToString()).ToArray()),
                RedisType.Set =>
                    JsonSerializer.Serialize((await db.SetMembersAsync(key)).Select(v => v.ToString()).ToArray()),
                RedisType.SortedSet =>
                    JsonSerializer.Serialize(
                        (await db.SortedSetRangeByScoreWithScoresAsync(key))
                            .Select(e => new { member = e.Element.ToString(), score = e.Score })
                            .ToArray()
                    ),
                RedisType.Hash
                    => JsonSerializer.Serialize(
                        (await db.HashGetAllAsync(key))
                            .ToDictionary(e => e.Name.ToString(), e => e.Value.ToString())
                    ),
                RedisType.None => "Key does not exist.",
                _ => $"Unknown type: {type}",
            };
            mux.Dispose();

            return result;
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 取得 key 的型別
    /// </summary>
    [McpServerTool, Description("取得 key 的型別")]
    public async Task<string> GetKeyType(
        [Description("Redis key 名稱")] string key,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            RedisType keyType = await db.KeyTypeAsync(key);

            return keyType.ToString().ToLowerInvariant();
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 取得 key 的剩餘存活時間（秒）
    /// </summary>
    [McpServerTool, Description("取得 key 的剩餘存活時間（秒）")]
    public async Task<string> GetKeyTtl(
        [Description("Redis key 名稱")] string key,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            TimeSpan? ttl = await db.KeyTimeToLiveAsync(key);

            if (ttl is null) {
                return "Key does not exist.";
            }

            return ttl == TimeSpan.MinValue ? "No expiry." : $"{ttl.Value.TotalSeconds:F0} seconds";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 取得 Redis 伺服器資訊（指定 section，預設 server）
    /// </summary>
    [McpServerTool, Description("取得 Redis 伺服器資訊（指定 section，預設 server）")]
    public async Task<string> GetInfo(
        [Description("INFO section（server/clients/memory/stats/replication/all 等）")] string section = "server",
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IServer server = mux.GetServers().First();
            IGrouping<string, KeyValuePair<string, string>>[] info = await server.InfoAsync(section);

            return string.Join("\n", info.SelectMany(g => g.Select(kv => $"{kv.Key}: {kv.Value}")));
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 設定 string key 的值（可選 TTL 秒數）
    /// </summary>
    [McpServerTool, Description("設定 string key 的值（可選 TTL 秒數）")]
    public async Task<string> SetKey(
        [Description("Redis key 名稱")] string key,
        [Description("要儲存的值")] string value,
        [Description("TTL 秒數，0 表示永不過期")] int ttlSeconds = 0,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();

            if (ttlSeconds > 0) {
                await db.StringSetAsync(key, value, TimeSpan.FromSeconds(ttlSeconds));
            } else {
                await db.StringSetAsync(key, value);
            }

            return ttlSeconds > 0 ? $"SET {key} (TTL={ttlSeconds}s)" : $"SET {key}";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 刪除一個或多個 key（以逗號分隔）
    /// </summary>
    [McpServerTool, Description("刪除一個或多個 key（以逗號分隔）")]
    public async Task<string> DeleteKeys(
        [Description("key 名稱，多個以逗號分隔")] string keys,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            RedisKey[] keyList = keys
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => new RedisKey(k))
                .ToArray();

            using IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            long deleted = await db.KeyDeleteAsync(keyList);

            return $"{deleted} key(s) deleted.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// 設定 key 的 TTL（秒）
    /// </summary>
    [McpServerTool, Description("設定 key 的 TTL（秒）")]
    public async Task<string> SetKeyTtl(
        [Description("Redis key 名稱")] string key,
        [Description("TTL 秒數")] int ttlSeconds,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            using IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            bool ok = await db.KeyExpireAsync(key, TimeSpan.FromSeconds(ttlSeconds));

            return ok ? $"Expire set to {ttlSeconds}s." : "Key does not exist.";
        } catch (Exception ex) {
            return $"Error: {ex.Message}";
        }
    }
}
