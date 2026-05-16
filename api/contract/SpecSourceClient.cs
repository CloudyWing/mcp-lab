namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Loads OpenAPI specifications from URL or local container path.
/// </summary>
public sealed class SpecSourceClient {
    private readonly ConnectionRegistry registry;

    /// <summary>
    /// Initializes a new instance of <see cref="SpecSourceClient"/>.
    /// </summary>
    public SpecSourceClient(ConnectionRegistry registry) {
        this.registry = registry;
    }

    /// <summary>
    /// Loads and parses a profile's OpenAPI specification.
    /// </summary>
    public async Task<OpenApiSpec> LoadSpecAsync(ConnectionConfig cfg) {
        string text = !string.IsNullOrWhiteSpace(cfg.SpecUrl)
            ? await LoadUrlAsync(cfg).ConfigureAwait(false)
            : await File.ReadAllTextAsync(cfg.SpecPath).ConfigureAwait(false);

        return OpenApiSpec.Parse(text);
    }

    private async Task<string> LoadUrlAsync(ConnectionConfig cfg) {
        using HttpClient http = registry.CreateClient(cfg);
        using HttpRequestMessage request = new(HttpMethod.Get, cfg.SpecUrl);
        ApplyAuthHeader(cfg, request);
        using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"OpenAPI spec endpoint returned HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}"
            );
        }

        return body;
    }

    private static void ApplyAuthHeader(ConnectionConfig cfg, HttpRequestMessage request) {
        if (string.IsNullOrWhiteSpace(cfg.AuthHeaderName) || string.IsNullOrWhiteSpace(cfg.AuthHeaderValue)) {
            return;
        }

        request.Headers.TryAddWithoutValidation(cfg.AuthHeaderName, cfg.AuthHeaderValue);
    }
}
