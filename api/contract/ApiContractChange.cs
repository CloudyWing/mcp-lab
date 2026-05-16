namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Represents a detected API contract change.
/// </summary>
public sealed record ApiContractChange(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("detail")] string Detail
);
