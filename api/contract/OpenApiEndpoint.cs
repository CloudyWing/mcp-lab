namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Represents a summarized OpenAPI operation.
/// </summary>
public sealed record OpenApiEndpoint(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("operation_id")] string OperationId,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("status_codes")] IReadOnlyList<string> StatusCodes,
    [property: JsonPropertyName("has_request_body")] bool HasRequestBody,
    [property: JsonPropertyName("request_body_required")] bool RequestBodyRequired
);
