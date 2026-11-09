using CloudyWing.McpLab.Shared.DotEnv;

namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Provides MCP tools for bounded OpenAPI inspection and drift checks.
/// </summary>
[McpServerToolType]
public sealed class ApiContractTools {
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private const int DefaultIssueLimit = 50;
    private const int MaxIssueLimit = 200;
    private readonly ConnectionRegistry registry;
    private readonly SpecSourceClient specClient;
    private readonly IDotEnvSnapshotProvider? snapshotProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="ApiContractTools"/>.
    /// </summary>
    /// <param name="registry">The connection registry.</param>
    /// <param name="specClient">The OpenAPI spec loader.</param>
    /// <param name="snapshotProvider">The .env snapshot provider used to read body size limits.</param>
    public ApiContractTools(
        ConnectionRegistry registry,
        SpecSourceClient specClient,
        IDotEnvSnapshotProvider? snapshotProvider = null
    ) {
        this.registry = registry;
        this.specClient = specClient;
        this.snapshotProvider = snapshotProvider;
    }

    private IReadOnlyDictionary<string, string> Settings =>
        snapshotProvider?.Current ?? new Dictionary<string, string>();

    private int MaxResponseBytes => ServiceSettings.Common.MaxResponseBytes.Read(Settings);

    /// <summary>
    /// Lists all configured API contract profiles.
    /// </summary>
    [McpServerTool, Description("列出所有已設定的 API contract profiles，不回傳 auth header value")]
    public string ListConnections() =>
        ToolResponse.Ok(registry.All.Select(kv => ToConnectionSummary(kv.Value)));

    /// <summary>
    /// Gets an OpenAPI specification summary.
    /// </summary>
    [McpServerTool, Description("讀取 OpenAPI spec 摘要")]
    public async Task<string> GetSpecSummary(
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            ConnectionConfig cfg = registry.Get(connection);
            object summary = await specClient.WithSpecAsync<object>(cfg, spec => {
                IReadOnlyList<OpenApiEndpoint> endpoints = spec.ListEndpoints(limit: MaxListLimit);

                return new {
                    connection = ToConnectionSummary(cfg),
                    title = spec.Title,
                    version = spec.InfoVersion,
                    openapi = spec.Version,
                    server_url = GetBaseUrl(cfg, spec),
                    paths = spec.Root["paths"]?.AsObject().Count ?? 0,
                    operations = endpoints.Count,
                    methods = endpoints
                        .GroupBy(endpoint => endpoint.Method)
                        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
                };
            }).ConfigureAwait(false);

            return ToolResponse.Ok(summary);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Lists OpenAPI endpoints.
    /// </summary>
    [McpServerTool, Description("列出 OpenAPI endpoints，可依 path prefix、method 或 tag 篩選")]
    public async Task<string> ListEndpoints(
        [Description("連線名稱，省略則使用第一個")] string connection = "",
        [Description("Path 前綴，空字串表示不篩選")] string pathPrefix = "",
        [Description("HTTP method，空字串表示不篩選")] string method = "",
        [Description("OpenAPI tag，空字串表示不篩選")] string tag = "",
        [Description("最大回傳數，預設 100，上限 500")] int limit = 0
    ) {
        try {
            int safeLimit = NormalizeLimit(limit);
            ConnectionConfig cfg = registry.Get(connection);
            IReadOnlyList<OpenApiEndpoint> endpoints = await specClient
                .WithSpecAsync(cfg, spec => spec.ListEndpoints(pathPrefix, method, tag, safeLimit))
                .ConfigureAwait(false);

            if (endpoints.Count == 0) {
                return ToolResponse.Empty("No endpoints found.", new { limit = safeLimit });
            }

            ResponseBudget budget = new(MaxResponseBytes);
            List<OpenApiEndpoint> kept = [];

            foreach (OpenApiEndpoint endpoint in endpoints) {
                if (!budget.TryAddJson(endpoint)) {
                    break;
                }

                kept.Add(endpoint);
            }

            return budget.Exhausted
                ? ToolResponse.Ok(
                    ResponseTruncation.Append(new { limit = safeLimit, endpoints = kept }, budget),
                    ResponseTruncation.Message(budget.ObservedBytes, false, budget.MaxBytes)
                )
                : ToolResponse.Ok(new { limit = safeLimit, endpoints });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Gets a single OpenAPI operation object.
    /// </summary>
    [McpServerTool, Description("取得指定 path 與 method 的 OpenAPI operation JSON")]
    public async Task<string> GetEndpointSchema(
        [Description("OpenAPI path，例如 /users/{id}")] string path,
        [Description("HTTP method，例如 GET")] string method,
        [Description("連線名稱，省略則使用第一個")] string connection = ""
    ) {
        try {
            ConnectionConfig cfg = registry.Get(connection);
            string operationJson = await specClient
                .WithSpecAsync(cfg, spec => spec.GetOperationJson(path, method))
                .ConfigureAwait(false);
            string upperMethod = method.ToUpperInvariant();
            int maxResponseBytes = MaxResponseBytes;
            int operationBytes = ResponseBudget.Utf8Size(operationJson);

            if (operationBytes <= maxResponseBytes) {
                return ToolResponse.Ok(new {
                    path,
                    method = upperMethod,
                    operation = JsonNode.Parse(operationJson),
                });
            }

            byte[] utf8 = Encoding.UTF8.GetBytes(operationJson);

            return ToolResponse.Ok(
                ResponseTruncation.Append(
                    new {
                        path,
                        method = upperMethod,
                        operation_json_prefix = Utf8Text.DecodePrefix(utf8.AsSpan(0, maxResponseBytes), truncated: true),
                    },
                    operationBytes,
                    true,
                    maxResponseBytes
                ),
                ResponseTruncation.Message(operationBytes, true, maxResponseBytes)
            );
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Compares two OpenAPI specifications.
    /// </summary>
    [McpServerTool, Description("比較 baseline 與 candidate OpenAPI spec 的 endpoint/status drift")]
    public async Task<string> CompareSpecs(
        [Description("Baseline 連線名稱")] string baselineConnection,
        [Description("Candidate 連線名稱")] string candidateConnection,
        [Description("是否只回傳 breaking changes")] bool breakingOnly = false
    ) {
        try {
            ConnectionConfig baselineCfg = registry.Get(baselineConnection);
            ConnectionConfig candidateCfg = registry.Get(candidateConnection);
            IReadOnlyDictionary<string, OpenApiEndpoint> baselineMap = await specClient
                .WithSpecAsync(baselineCfg, spec => spec.GetEndpointMap())
                .ConfigureAwait(false);
            IReadOnlyDictionary<string, OpenApiEndpoint> candidateMap = await specClient
                .WithSpecAsync(candidateCfg, spec => spec.GetEndpointMap())
                .ConfigureAwait(false);
            IReadOnlyList<ApiContractChange> changes = OpenApiDriftDetector.Compare(baselineMap, candidateMap);

            if (breakingOnly) {
                changes = changes
                    .Where(change => change.Severity.Equals("breaking", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            int breakingChanges = changes.Count(change => change.Severity == "breaking");
            int totalChanges = changes.Count;
            ResponseBudget budget = new(MaxResponseBytes);
            List<ApiContractChange> kept = [];

            foreach (ApiContractChange change in changes) {
                if (!budget.TryAddJson(change)) {
                    break;
                }

                kept.Add(change);
            }

            var data = new {
                baseline = baselineCfg.Name,
                candidate = candidateCfg.Name,
                breaking_only = breakingOnly,
                breaking_changes = breakingChanges,
                total_changes = totalChanges,
                changes = (IReadOnlyList<ApiContractChange>)kept,
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
    /// Invokes a configured API endpoint when enabled and method allow-list enforcement passes.
    /// </summary>
    [McpServerTool, Description("依 profile 呼叫 API endpoint；預設關閉，需設定 INVOKE_ENABLED=true 並通過 method allow-list")]
    public async Task<string> InvokeEndpoint(
        [Description("OpenAPI path template 或實際 path")] string path,
        [Description("HTTP method，預設 GET")] string method = "GET",
        [Description("連線名稱，省略則使用第一個")] string connection = "",
        [Description("Path parameter JSON object，例如 {\"id\":\"1\"}")] string pathParametersJson = "",
        [Description("Query parameter JSON object")] string queryJson = "",
        [Description("額外 header JSON object，不應傳入機密")] string headersJson = "",
        [Description("Request body JSON，非 safe method 才會使用")] string bodyJson = "",
        [Description("最大回傳 body bytes，預設與上限由 API_CONTRACT_DEFAULT_BODY_BYTES、API_CONTRACT_MAX_BODY_BYTES 設定")] int maxBodyBytes = 0
    ) {
        try {
            string normalizedMethod = method.ToUpperInvariant();
            int maxBodyLimit = ServiceSettings.ApiContract.MaxBodyBytes.Read(Settings);
            int safeMaxBodyBytes = ToolRuntimeOptions.NormalizeRequestedInt32(
                maxBodyBytes,
                Math.Min(ServiceSettings.ApiContract.DefaultBodyBytes.Read(Settings), maxBodyLimit),
                1,
                maxBodyLimit
            );
            ConnectionConfig cfg = registry.Get(connection);

            if (!cfg.InvokeEnabled) {
                return ToolResponse.Blocked(
                    $"invoke_endpoint is disabled for profile '{cfg.Name}'.",
                    new {
                        connection = cfg.Name,
                        required_setting = "API_CONTRACT_CONN_<ALIAS>_INVOKE_ENABLED=true",
                    }
                );
            }

            if (!cfg.AllowedMethods.Contains(normalizedMethod)) {
                return ToolResponse.Blocked(
                    $"{normalizedMethod} is not allowed for profile '{cfg.Name}'.",
                    new { allowed_methods = cfg.AllowedMethods }
                );
            }

            (string baseUrl, IReadOnlyList<string> responseKeys) = await specClient
                .WithSpecAsync(cfg, spec => (GetBaseUrl(cfg, spec), spec.GetResponseKeys(path, normalizedMethod)))
                .ConfigureAwait(false);
            Uri url = BuildUrl(baseUrl, path, pathParametersJson, queryJson);
            using HttpClient http = registry.CreateClient(cfg);
            using HttpRequestMessage request = new(new HttpMethod(normalizedMethod), url);
            ApplyAuthAndHeaders(cfg, request, headersJson);

            if (!string.IsNullOrWhiteSpace(bodyJson) && !IsSafeMethod(normalizedMethod)) {
                request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            }

            int maxResponseBytes = MaxResponseBytes;
            using BoundedResponse bounded = await BoundedHttpContent
                .SendAsync(http, request, Math.Min(safeMaxBodyBytes, maxResponseBytes))
                .ConfigureAwait(false);
            HttpResponseMessage response = bounded.Response;
            bool truncated = bounded.Body.Truncated;
            string body = bounded.Body.ToText();
            var data = new {
                url = url.ToString(),
                method = normalizedMethod,
                status_code = (int)response.StatusCode,
                status_defined_in_spec = OpenApiSpec.IsStatusDefined(responseKeys, (int)response.StatusCode),
                content_type = response.Content.Headers.ContentType?.MediaType ?? "",
                redirect_location = response.Headers.Location?.ToString(),
                body_truncated = truncated,
                body,
            };

            return CreateInvokeResponse(data, bounded.Body, safeMaxBodyBytes, maxResponseBytes);
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    /// <summary>
    /// Creates the <c>invoke_endpoint</c> response. When the body was cut, the truncation fields report the limit
    /// that was in effect, which is the smaller of <paramref name="maxBodyBytes"/> and <paramref name="maxResponseBytes"/>.
    /// </summary>
    /// <param name="data">The response data that serializes to a JSON object.</param>
    /// <param name="body">The body that was read with the effective limit.</param>
    /// <param name="maxBodyBytes">The body limit of the call.</param>
    /// <param name="maxResponseBytes">The configured response limit.</param>
    public static string CreateInvokeResponse(object data, BoundedBody body, int maxBodyBytes, int maxResponseBytes) {
        if (!body.Truncated) {
            return ToolResponse.Ok(data);
        }

        // 回應本文被截斷：任一限制（maxBodyBytes 或 MCPLAB_MAX_RESPONSE_BYTES）造成截斷皆附上截斷欄位，
        // max_response_bytes 回報實際生效的上限。
        int effectiveLimit = Math.Min(maxBodyBytes, maxResponseBytes);

        return maxBodyBytes >= maxResponseBytes
            ? ToolResponse.Ok(
                ResponseTruncation.Append(data, body, effectiveLimit),
                ResponseTruncation.Message(body.OriginalBytes, body.OriginalBytesExact, effectiveLimit)
            )
            : ToolResponse.Ok(ResponseTruncation.Append(data, body, effectiveLimit));
    }

    /// <summary>
    /// Performs bounded response checks against the OpenAPI response schema when available.
    /// </summary>
    [McpServerTool, Description("以有限 OpenAPI response schema 檢查 status code 與 JSON body")]
    public async Task<string> ValidateResponse(
        [Description("OpenAPI path")] string path,
        [Description("HTTP method")] string method,
        [Description("HTTP status code")] int statusCode,
        [Description("Response body JSON")] string responseBody,
        [Description("連線名稱，省略則使用第一個")] string connection = "",
        [Description("最大回傳 issue 數，預設 50，上限 200")] int maxIssues = 0
    ) {
        try {
            int safeMaxIssues = ToolRuntimeOptions.NormalizeRequestedInt32(
                maxIssues,
                DefaultIssueLimit,
                1,
                MaxIssueLimit
            );
            ConnectionConfig cfg = registry.Get(connection);
            IReadOnlyList<string> issues = await specClient.WithSpecAsync(
                cfg,
                spec => ApiResponseValidator.Validate(
                    spec,
                    path,
                    method,
                    statusCode,
                    responseBody,
                    safeMaxIssues
                )
            ).ConfigureAwait(false);

            return ToolResponse.Ok(new {
                valid = issues.Count == 0,
                issue_limit = safeMaxIssues,
                issues,
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    private object ToConnectionSummary(ConnectionConfig cfg) =>
        new {
            environment = registry.EnvironmentOf(cfg.Name),
            name = cfg.Name,
            spec_source = string.IsNullOrWhiteSpace(cfg.SpecUrl) ? "path" : "url",
            spec_url = cfg.SpecUrl,
            spec_path = cfg.SpecPath,
            base_url = cfg.BaseUrl,
            invoke_enabled = cfg.InvokeEnabled,
            allowed_methods = cfg.AllowedMethods,
            ssl_skip_verify = cfg.SslSkipVerify,
            auth_header_configured = !string.IsNullOrWhiteSpace(cfg.AuthHeaderName)
                && !string.IsNullOrWhiteSpace(cfg.AuthHeaderValue),
        };

    private static int NormalizeLimit(int limit) =>
        ToolRuntimeOptions.NormalizeRequestedInt32(limit, DefaultListLimit, 1, MaxListLimit);

    private static string GetBaseUrl(ConnectionConfig cfg, OpenApiSpec spec) {
        string baseUrl = string.IsNullOrWhiteSpace(cfg.BaseUrl) ? spec.GetFirstServerUrl() : cfg.BaseUrl;

        if (string.IsNullOrWhiteSpace(baseUrl)) {
            throw new InvalidOperationException("Base URL is not configured and the OpenAPI spec has no servers[0].url.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? _)) {
            throw new InvalidOperationException($"Base URL must be absolute: {baseUrl}");
        }

        return baseUrl.TrimEnd('/');
    }

    private static Uri BuildUrl(string baseUrl, string path, string pathParametersJson, string queryJson) {
        string resolvedPath = ApplyPathParameters(path, ParseObject(pathParametersJson));
        UriBuilder builder = new($"{baseUrl}/{resolvedPath.TrimStart('/')}");
        JsonObject query = ParseObject(queryJson);
        string queryString = string.Join(
            "&",
            query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value?.ToString() ?? "")}")
        );

        if (!string.IsNullOrWhiteSpace(queryString)) {
            builder.Query = queryString;
        }

        return builder.Uri;
    }

    private static string ApplyPathParameters(string path, JsonObject parameters) {
        string result = path;

        foreach ((string name, JsonNode? value) in parameters) {
            result = result.Replace(
                "{" + name + "}",
                Uri.EscapeDataString(value?.ToString() ?? ""),
                StringComparison.Ordinal
            );
        }

        if (result.Contains('{', StringComparison.Ordinal) || result.Contains('}', StringComparison.Ordinal)) {
            throw new InvalidOperationException("Path contains unresolved OpenAPI path parameters.");
        }

        return result;
    }

    private static void ApplyAuthAndHeaders(ConnectionConfig cfg, HttpRequestMessage request, string headersJson) {
        if (!string.IsNullOrWhiteSpace(cfg.AuthHeaderName) && !string.IsNullOrWhiteSpace(cfg.AuthHeaderValue)) {
            request.Headers.TryAddWithoutValidation(cfg.AuthHeaderName, cfg.AuthHeaderValue);
        }

        foreach ((string name, JsonNode? value) in ParseObject(headersJson)) {
            request.Headers.TryAddWithoutValidation(name, value?.ToString() ?? "");
        }
    }

    private static JsonObject ParseObject(string json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return [];
        }

        JsonNode? node = JsonNode.Parse(json);

        if (node is not JsonObject obj) {
            throw new InvalidOperationException("JSON input must be an object.");
        }

        return obj;
    }

    private static bool IsSafeMethod(string method) =>
        method.Equals("GET", StringComparison.OrdinalIgnoreCase)
        || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
        || method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase);
}
