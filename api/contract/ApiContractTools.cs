namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Provides MCP tools for bounded OpenAPI inspection and drift checks.
/// </summary>
[McpServerToolType]
public sealed class ApiContractTools {
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private const int DefaultBodyLimit = 50000;
    private const int MaxBodyLimit = 200000;
    private const int DefaultIssueLimit = 50;
    private const int MaxIssueLimit = 200;
    private readonly ConnectionRegistry registry;
    private readonly SpecSourceClient specClient;

    /// <summary>
    /// Initializes a new instance of <see cref="ApiContractTools"/>.
    /// </summary>
    public ApiContractTools(ConnectionRegistry registry, SpecSourceClient specClient) {
        this.registry = registry;
        this.specClient = specClient;
    }

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
            OpenApiSpec spec = await specClient.LoadSpecAsync(cfg).ConfigureAwait(false);
            IReadOnlyList<OpenApiEndpoint> endpoints = spec.ListEndpoints(limit: MaxListLimit);

            return ToolResponse.Ok(new {
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
            });
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
            OpenApiSpec spec = await specClient.LoadSpecAsync(cfg).ConfigureAwait(false);
            IReadOnlyList<OpenApiEndpoint> endpoints = spec.ListEndpoints(pathPrefix, method, tag, safeLimit);

            return endpoints.Count == 0
                ? ToolResponse.Empty("No endpoints found.", new { limit = safeLimit })
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
            OpenApiSpec spec = await specClient.LoadSpecAsync(cfg).ConfigureAwait(false);

            return ToolResponse.Ok(new {
                path,
                method = method.ToUpperInvariant(),
                operation = JsonNode.Parse(spec.GetOperationJson(path, method)),
            });
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
            OpenApiSpec baseline = await specClient.LoadSpecAsync(baselineCfg).ConfigureAwait(false);
            OpenApiSpec candidate = await specClient.LoadSpecAsync(candidateCfg).ConfigureAwait(false);
            IReadOnlyList<ApiContractChange> changes = OpenApiDriftDetector.Compare(baseline, candidate);

            if (breakingOnly) {
                changes = changes
                    .Where(change => change.Severity.Equals("breaking", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            return ToolResponse.Ok(new {
                baseline = baselineCfg.Name,
                candidate = candidateCfg.Name,
                breaking_only = breakingOnly,
                breaking_changes = changes.Count(change => change.Severity == "breaking"),
                total_changes = changes.Count,
                changes,
            });
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
        [Description("最大回傳 body bytes，預設 50000，上限 200000")] int maxBodyBytes = 0
    ) {
        try {
            string normalizedMethod = method.ToUpperInvariant();
            int safeMaxBodyBytes = ToolRuntimeOptions.NormalizeRequestedInt32(
                maxBodyBytes,
                DefaultBodyLimit,
                1,
                MaxBodyLimit
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

            OpenApiSpec spec = await specClient.LoadSpecAsync(cfg).ConfigureAwait(false);
            Uri url = BuildUrl(GetBaseUrl(cfg, spec), path, pathParametersJson, queryJson);
            using HttpClient http = registry.CreateClient(cfg);
            using HttpRequestMessage request = new(new HttpMethod(normalizedMethod), url);
            ApplyAuthAndHeaders(cfg, request, headersJson);

            if (!string.IsNullOrWhiteSpace(bodyJson) && !IsSafeMethod(normalizedMethod)) {
                request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            }

            using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            bool truncated = bytes.Length > safeMaxBodyBytes;
            string body = Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, safeMaxBodyBytes)));

            return ToolResponse.Ok(new {
                url = url.ToString(),
                method = normalizedMethod,
                status_code = (int)response.StatusCode,
                status_defined_in_spec = IsStatusDefined(spec, path, normalizedMethod, (int)response.StatusCode),
                content_type = response.Content.Headers.ContentType?.MediaType ?? "",
                body_truncated = truncated,
                body,
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
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
            OpenApiSpec spec = await specClient.LoadSpecAsync(cfg).ConfigureAwait(false);
            IReadOnlyList<string> issues = ApiResponseValidator.Validate(
                spec,
                path,
                method,
                statusCode,
                responseBody,
                safeMaxIssues
            );

            return ToolResponse.Ok(new {
                valid = issues.Count == 0,
                issue_limit = safeMaxIssues,
                issues,
            });
        } catch (Exception ex) {
            return ToolResponse.Error(ex);
        }
    }

    private static object ToConnectionSummary(ConnectionConfig cfg) =>
        new {
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

    private static bool IsStatusDefined(OpenApiSpec spec, string path, string method, int statusCode) {
        try {
            return spec.GetResponse(path, method, statusCode) is not null;
        } catch (KeyNotFoundException) {
            return false;
        }
    }
}
