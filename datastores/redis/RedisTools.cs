namespace CloudyWing.McpLab.Redis;

/// <summary>
/// Provides MCP tools for reading from and writing to Redis instances.
/// </summary>
[McpServerToolType]
public sealed class RedisTools {
    private static readonly JsonSerializerOptions JsonCompact = new() { WriteIndented = false };
    private readonly ConnectionRegistry registry;
    private readonly RedisToolLimits limits;

    /// <summary>
    /// Initializes a new instance of <see cref="RedisTools"/>.
    /// </summary>
    /// <param name="registry">The connection registry.</param>
    /// <param name="limits">The Redis tool limits.</param>
    public RedisTools(ConnectionRegistry registry, RedisToolLimits limits) {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(limits);

        this.registry = registry;
        this.limits = limits;
    }

    /// <summary>
    /// 列出所有已設定的 Redis 連線
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 Redis 連線")]
    public string ListConnections() =>
        ToolResponse.Ok(
            registry.All.Select(kv => new {
                name = kv.Key,
                environment = registry.EnvironmentOf(kv.Key),
                host = kv.Value.Host,
                port = kv.Value.Port,
                database = kv.Value.Database,
            })
        );

    /// <summary>
    /// 列出目前生效的 Redis runtime limit 設定。
    /// </summary>
    [McpServerTool, Description("列出目前生效的 Redis runtime limit 設定")]
    public string ListRuntimeLimits() =>
        ToolResponse.Ok(new {
            max_rows = limits.KeyCount,
            max_response_bytes = limits.MaxResponseBytes,
        });

    /// <summary>
    /// 測試 Redis 連線是否正常（PING）
    /// </summary>
    [McpServerTool, Description("測試 Redis 連線是否正常（PING）")]
    public async Task<string> PingConnection(
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            TimeSpan latency = await db.PingAsync();

            return ToolResponse.Ok(new {
                latency_ms = latency.TotalMilliseconds,
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 列出符合 pattern 的 keys（預設最多 100 個）
    /// </summary>
    [McpServerTool, Description("列出符合 pattern 的 keys（預設最多 100 個）")]
    public async Task<string> ListKeys(
        [Description("Glob 模式，預設 *")] string pattern = "*",
        [Description("最多回傳筆數，允許 1 到 1000")] int count = 100,
        [Description("連線名稱")] string connection = ""
    ) {
        try {
            int safeCount = limits.NormalizeKeyCount(count);
            IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IServer server = mux.GetServers().First();
            List<string> keys = [];

            await foreach (RedisKey key in server.KeysAsync(pattern: pattern, pageSize: safeCount)) {
                keys.Add(key.ToString());

                if (keys.Count >= safeCount) {
                    break;
                }
            }

            return keys.Count > 0
                ? ToolResponse.Ok(keys)
                : ToolResponse.Empty("No keys found.", keys);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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
            IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            RedisType type = await db.KeyTypeAsync(key);
            int max = limits.KeyCount;
            int maxBytes = limits.MaxResponseBytes;

            if (type == RedisType.None) {
                return ToolResponse.Empty("Key does not exist.");
            }

            if (type == RedisType.String) {
                (string text, long originalBytes, bool valueTruncated) = await RedisValueReader.ReadStringAsync(db, key, maxBytes);
                var stringData = new {
                    key,
                    type = "string",
                    value = text,
                };

                return valueTruncated
                    ? ToolResponse.Ok(
                        ResponseTruncation.Append(stringData, originalBytes, true, maxBytes),
                        ResponseTruncation.Message(originalBytes, true, maxBytes)
                    )
                    : ToolResponse.Ok(stringData);
            }

            ResponseBudget budget = new(maxBytes);
            (object value, long total, int returned) = await RedisValueReader.ReadCollectionAsync(db, type, key, max, budget);
            var data = new {
                key,
                type = type.ToString().ToLowerInvariant(),
                value,
                total_count = total,
                returned_count = returned,
                truncated = total > returned,
                max_items = max,
            };

            return budget.Exhausted
                ? ToolResponse.Ok(
                    ResponseTruncation.Append(data, budget),
                    ResponseTruncation.Message(budget.ObservedBytes, false, budget.MaxBytes)
                )
                : ToolResponse.Ok(data);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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
            IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            RedisType keyType = await db.KeyTypeAsync(key);

            return ToolResponse.Ok(new {
                key,
                type = keyType.ToString().ToLowerInvariant(),
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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
            IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            TimeSpan? ttl = await db.KeyTimeToLiveAsync(key);

            if (ttl is null) {
                return ToolResponse.Empty("Key does not exist.");
            }

            return ToolResponse.Ok(new {
                key,
                ttl_seconds = ttl == TimeSpan.MinValue ? (double?)null : ttl.Value.TotalSeconds,
                has_expiry = ttl != TimeSpan.MinValue,
            }, ttl == TimeSpan.MinValue ? "No expiry." : "TTL returned.");
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
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
            IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IServer server = mux.GetServers().First();
            IGrouping<string, KeyValuePair<string, string>>[] info = await server.InfoAsync(section);

            return ToolResponse.Ok(info.ToDictionary(
                group => group.Key,
                group => group.ToDictionary(kv => kv.Key, kv => kv.Value)
            ));
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 設定 string key 的值（可選 TTL 秒數）
    /// </summary>
    [McpServerTool, Description("設定 string key 的值（可選 TTL 秒數）")]
    public async Task<string> SetKey(
        [Description("Redis key 名稱")] string key,
        [Description("要儲存的值")] string value,
        [Description("TTL 秒數，0 表示永不過期，負數不允許")] int ttlSeconds = 0,
        [Description(WriteConnectionGuard.ParameterDescription)] string connection = ""
    ) {
        if (WriteConnectionGuard.CheckMissing(connection) is string blocked) {
            return blocked;
        }

        try {
            if (!limits.IsSetKeyTtlValid(ttlSeconds)) {
                return ToolResponse.Error("TTL seconds must be zero or greater.");
            }

            IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();

            if (ttlSeconds > 0) {
                await db.StringSetAsync(key, value, TimeSpan.FromSeconds(ttlSeconds));
            } else {
                await db.StringSetAsync(key, value);
            }

            return ToolResponse.Ok(new {
                key,
                ttl_seconds = ttlSeconds > 0 ? (int?)ttlSeconds : null,
            }, "Key set.");
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 刪除一個或多個 key（以逗號分隔）
    /// </summary>
    [McpServerTool, Description("刪除一個或多個 key（以逗號分隔）")]
    public async Task<string> DeleteKeys(
        [Description("key 名稱，多個以逗號分隔")] string keys,
        [Description(WriteConnectionGuard.ParameterDescription)] string connection = ""
    ) {
        if (WriteConnectionGuard.CheckMissing(connection) is string blocked) {
            return blocked;
        }

        try {
            RedisKey[] keyList = keys
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => new RedisKey(k))
                .ToArray();

            if (keyList.Length == 0) {
                return ToolResponse.Error("At least one key is required.");
            }

            IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            long deleted = await db.KeyDeleteAsync(keyList);

            return ToolResponse.Ok(new {
                deleted,
            }, "Keys deleted.");
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// 設定 key 的 TTL（秒）
    /// </summary>
    [McpServerTool, Description("設定 key 的 TTL（秒）")]
    public async Task<string> SetKeyTtl(
        [Description("Redis key 名稱")] string key,
        [Description("TTL 秒數，必須大於 0")] int ttlSeconds,
        [Description(WriteConnectionGuard.ParameterDescription)] string connection = ""
    ) {
        if (WriteConnectionGuard.CheckMissing(connection) is string blocked) {
            return blocked;
        }

        try {
            if (!limits.IsExpireTtlValid(ttlSeconds)) {
                return ToolResponse.Error("TTL seconds must be greater than zero.");
            }

            IConnectionMultiplexer mux = await registry.ConnectAsync(connection);
            IDatabase db = mux.GetDatabase();
            bool ok = await db.KeyExpireAsync(key, TimeSpan.FromSeconds(ttlSeconds));

            return ok
                ? ToolResponse.Ok(new {
                    key,
                    ttl_seconds = ttlSeconds,
                }, "Expiry set.")
                : ToolResponse.Empty("Key does not exist.");
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }
}
