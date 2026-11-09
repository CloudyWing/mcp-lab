namespace CloudyWing.McpLab.ApiContract;

/// <summary>
/// Loads OpenAPI specifications from URL or local container path.
/// </summary>
public sealed class SpecSourceClient {
    /// <summary>
    /// Maximum OpenAPI spec size read from a URL or file.
    /// </summary>
    public const int MaxSpecBytes = 10 * 1024 * 1024;

    private const int ErrorBodyPreviewBytes = 2048;
    private const int ErrorBodyPreviewChars = 500;

    private readonly ConnectionRegistry registry;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="SpecSourceClient"/>.
    /// </summary>
    public SpecSourceClient(ConnectionRegistry registry) {
        this.registry = registry;
    }

    /// <summary>
    /// Loads and parses a profile's OpenAPI specification, runs <paramref name="action"/> on it, and releases it.
    /// Only one parsed specification is alive at a time because callers are serialized.
    /// </summary>
    /// <typeparam name="T">The type of the result of <paramref name="action"/>.</typeparam>
    /// <param name="cfg">The profile that names the specification source.</param>
    /// <param name="action">Extracts what the caller needs. The result must not hold on to the specification.</param>
    /// <param name="cancellationToken">Cancels waiting for the gate and loading the specification.</param>
    public async Task<T> WithSpecAsync<T>(
        ConnectionConfig cfg,
        Func<OpenApiSpec, T> action,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentNullException.ThrowIfNull(action);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            OpenApiSpec spec = await LoadSpecAsync(cfg, cancellationToken).ConfigureAwait(false);

            return action(spec);
        } finally {
            gate.Release();
        }
    }

    private async Task<OpenApiSpec> LoadSpecAsync(ConnectionConfig cfg, CancellationToken cancellationToken) {
        byte[] bytes = !string.IsNullOrWhiteSpace(cfg.SpecUrl)
            ? await LoadUrlAsync(cfg, cancellationToken).ConfigureAwait(false)
            : await LoadFileAsync(cfg.SpecPath, cancellationToken).ConfigureAwait(false);

        return OpenApiSpec.ParseUtf8(bytes);
    }

    private async Task<byte[]> LoadUrlAsync(ConnectionConfig cfg, CancellationToken cancellationToken) {
        using HttpClient http = registry.CreateClient(cfg);
        using HttpRequestMessage request = new(HttpMethod.Get, cfg.SpecUrl);
        ApplyAuthHeader(cfg, request);
        using BoundedResponse response = await BoundedHttpContent
            .SendAsync(http, request, MaxSpecBytes, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            byte[] bytes = response.Body.Bytes;
            int previewLength = Math.Min(bytes.Length, ErrorBodyPreviewBytes);
            string preview = Utf8Text.DecodePrefix(bytes.AsSpan(0, previewLength), bytes.Length > previewLength);

            throw new InvalidOperationException(
                $"OpenAPI spec endpoint returned HTTP {(int)response.Response.StatusCode}: {preview[..Math.Min(preview.Length, ErrorBodyPreviewChars)]}"
            );
        }

        if (response.Body.Truncated) {
            throw new InvalidOperationException($"OpenAPI spec exceeds {MaxSpecBytes} bytes.");
        }

        return response.Body.Bytes;
    }

    private static async Task<byte[]> LoadFileAsync(string path, CancellationToken cancellationToken) {
        FileInfo file = new(path);

        if (file.Exists && file.Length > MaxSpecBytes) {
            throw new InvalidOperationException($"OpenAPI spec exceeds {MaxSpecBytes} bytes.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyAuthHeader(ConnectionConfig cfg, HttpRequestMessage request) {
        if (string.IsNullOrWhiteSpace(cfg.AuthHeaderName) || string.IsNullOrWhiteSpace(cfg.AuthHeaderValue)) {
            return;
        }

        request.Headers.TryAddWithoutValidation(cfg.AuthHeaderName, cfg.AuthHeaderValue);
    }
}
