namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Loads OpenAPI specifications from URL or local container path.
/// </summary>
public sealed class SpecSourceClient {
    /// <summary>
    /// Maximum OpenAPI spec size read from a URL or file.
    /// </summary>
    public const int MaxSpecBytes = 10 * 1024 * 1024;

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
            : await LoadFileAsync(cfg.SpecPath).ConfigureAwait(false);

        return OpenApiSpec.Parse(text);
    }

    private async Task<string> LoadUrlAsync(ConnectionConfig cfg) {
        using HttpClient http = registry.CreateClient(cfg);
        using HttpRequestMessage request = new(HttpMethod.Get, cfg.SpecUrl);
        ApplyAuthHeader(cfg, request);
        using HttpResponseMessage response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        (byte[] bytes, bool truncated) = await BoundedHttpContent
            .ReadAsync(response.Content, MaxSpecBytes)
            .ConfigureAwait(false);
        string body = Encoding.UTF8.GetString(bytes);

        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"OpenAPI spec endpoint returned HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}"
            );
        }

        if (truncated) {
            throw new InvalidOperationException($"OpenAPI spec exceeds {MaxSpecBytes} bytes.");
        }

        return body;
    }

    private static async Task<string> LoadFileAsync(string path) {
        FileInfo file = new(path);

        if (file.Exists && file.Length > MaxSpecBytes) {
            throw new InvalidOperationException($"OpenAPI spec exceeds {MaxSpecBytes} bytes.");
        }

        return await File.ReadAllTextAsync(path).ConfigureAwait(false);
    }

    private static void ApplyAuthHeader(ConnectionConfig cfg, HttpRequestMessage request) {
        if (string.IsNullOrWhiteSpace(cfg.AuthHeaderName) || string.IsNullOrWhiteSpace(cfg.AuthHeaderValue)) {
            return;
        }

        request.Headers.TryAddWithoutValidation(cfg.AuthHeaderName, cfg.AuthHeaderValue);
    }
}
