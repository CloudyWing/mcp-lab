namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Represents a parsed OpenAPI JSON or YAML document.
/// </summary>
public sealed class OpenApiSpec {
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer YamlJsonSerializer = new SerializerBuilder().JsonCompatible().Build();

    private OpenApiSpec(JsonObject root) {
        Root = root;
    }

    /// <summary>
    /// Gets the underlying OpenAPI root object.
    /// </summary>
    public JsonObject Root { get; }

    /// <summary>
    /// Gets the OpenAPI version string.
    /// </summary>
    public string Version => GetString(Root, "openapi");

    /// <summary>
    /// Gets the API title.
    /// </summary>
    public string Title => GetString(Root["info"] as JsonObject, "title");

    /// <summary>
    /// Gets the API info version.
    /// </summary>
    public string InfoVersion => GetString(Root["info"] as JsonObject, "version");

    /// <summary>
    /// Parses an OpenAPI JSON or YAML document.
    /// </summary>
    public static OpenApiSpec Parse(string text) {
        JsonNode? node = TryParseJson(text) ?? ParseYaml(text);

        if (node is not JsonObject root) {
            throw new InvalidOperationException("OpenAPI document root must be an object.");
        }

        if (root["paths"] is not JsonObject) {
            throw new InvalidOperationException("OpenAPI document must contain a paths object.");
        }

        return new OpenApiSpec(root);
    }

    /// <summary>
    /// Enumerates summarized operations.
    /// </summary>
    public IReadOnlyList<OpenApiEndpoint> ListEndpoints(
        string pathPrefix = "",
        string method = "",
        string tag = "",
        int limit = 100
    ) {
        return EnumerateEndpoints()
            .Where(endpoint => string.IsNullOrWhiteSpace(pathPrefix)
                || endpoint.Path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(endpoint => string.IsNullOrWhiteSpace(method)
                || string.Equals(endpoint.Method, method, StringComparison.OrdinalIgnoreCase))
            .Where(endpoint => string.IsNullOrWhiteSpace(tag)
                || endpoint.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Take(limit)
            .ToArray();
    }

    /// <summary>
    /// Gets the operation object for a path and method.
    /// </summary>
    public JsonObject GetOperation(string path, string method) {
        JsonObject paths = GetPaths();

        if (paths[path] is not JsonObject pathItem) {
            throw new KeyNotFoundException($"Path '{path}' was not found in the OpenAPI document.");
        }

        string methodKey = method.ToLowerInvariant();

        if (pathItem[methodKey] is not JsonObject operation) {
            throw new KeyNotFoundException($"Operation {method.ToUpperInvariant()} {path} was not found.");
        }

        return operation;
    }

    /// <summary>
    /// Gets the first configured server URL, or an empty string when absent.
    /// </summary>
    public string GetFirstServerUrl() {
        if (Root["servers"] is not JsonArray servers || servers.Count == 0) {
            return "";
        }

        return servers[0] is JsonObject first ? GetString(first, "url") : "";
    }

    /// <summary>
    /// Gets the raw JSON for an operation.
    /// </summary>
    public string GetOperationJson(string path, string method) =>
        GetOperation(path, method).ToJsonString(JsonOptions);

    /// <summary>
    /// Gets the response object for an operation and status code.
    /// </summary>
    public JsonObject? GetResponse(string path, string method, int statusCode) {
        JsonObject operation = GetOperation(path, method);

        if (operation["responses"] is not JsonObject responses) {
            return null;
        }

        string statusKey = statusCode.ToString();

        if (responses[statusKey] is JsonObject exact) {
            return exact;
        }

        return responses["default"] as JsonObject;
    }

    /// <summary>
    /// Gets operations keyed by method and path.
    /// </summary>
    public IReadOnlyDictionary<string, OpenApiEndpoint> GetEndpointMap() =>
        EnumerateEndpoints().ToDictionary(
            endpoint => $"{endpoint.Method.ToUpperInvariant()} {endpoint.Path}",
            StringComparer.OrdinalIgnoreCase
        );

    private IEnumerable<OpenApiEndpoint> EnumerateEndpoints() {
        foreach ((string path, JsonNode? pathNode) in GetPaths()) {
            if (pathNode is not JsonObject pathItem) {
                continue;
            }

            foreach ((string method, JsonNode? operationNode) in pathItem) {
                if (!IsHttpMethod(method) || operationNode is not JsonObject operation) {
                    continue;
                }

                yield return new OpenApiEndpoint(
                    path,
                    method.ToUpperInvariant(),
                    GetString(operation, "operationId"),
                    GetString(operation, "summary"),
                    GetStringArray(operation["tags"] as JsonArray),
                    GetStatusCodes(operation),
                    operation["requestBody"] is JsonObject,
                    (operation["requestBody"] as JsonObject)?["required"]?.GetValue<bool>() ?? false
                );
            }
        }
    }

    private JsonObject GetPaths() => (JsonObject)Root["paths"]!;

    private static IReadOnlyList<string> GetStatusCodes(JsonObject operation) {
        if (operation["responses"] is not JsonObject responses) {
            return [];
        }

        return responses.Select(property => property.Key).Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> GetStringArray(JsonArray? array) {
        if (array is null) {
            return [];
        }

        return array
            .Select(node => node?.GetValue<string>() ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string GetString(JsonObject? obj, string name) =>
        obj?[name]?.GetValue<string>() ?? "";

    private static bool IsHttpMethod(string name) =>
        name.Equals("get", StringComparison.OrdinalIgnoreCase)
        || name.Equals("put", StringComparison.OrdinalIgnoreCase)
        || name.Equals("post", StringComparison.OrdinalIgnoreCase)
        || name.Equals("delete", StringComparison.OrdinalIgnoreCase)
        || name.Equals("patch", StringComparison.OrdinalIgnoreCase)
        || name.Equals("head", StringComparison.OrdinalIgnoreCase)
        || name.Equals("options", StringComparison.OrdinalIgnoreCase)
        || name.Equals("trace", StringComparison.OrdinalIgnoreCase);

    private static JsonNode? TryParseJson(string text) {
        try {
            return JsonNode.Parse(text);
        } catch (JsonException) {
            return null;
        }
    }

    private static JsonNode? ParseYaml(string text) {
        object? yaml = YamlDeserializer.Deserialize(new StringReader(text));
        string json = YamlJsonSerializer.Serialize(yaml);
        return JsonNode.Parse(json);
    }
}
