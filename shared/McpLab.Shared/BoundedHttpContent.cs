namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Reads HTTP response bodies without buffering more than a fixed number of bytes.
/// </summary>
public static class BoundedHttpContent {
    private const int ChunkSize = 16 * 1024;

    /// <summary>
    /// Reads up to <paramref name="maxBytes"/> bytes and reports whether the body was longer.
    /// The remaining stream is not drained.
    /// </summary>
    public static async Task<BoundedBody> ReadAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        long? declaredLength = content.Headers.ContentLength;
        bool lengthIsExact = declaredLength.HasValue && content.Headers.ContentEncoding.Count == 0;
        bool sizeKnown = lengthIsExact && declaredLength!.Value <= maxBytes;
        int capacity = lengthIsExact
            ? (int)Math.Min(declaredLength!.Value, maxBytes)
            : Math.Min(maxBytes, ChunkSize);

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[capacity];
        int written = 0;
        long total = 0;
        bool truncated = false;

        while (true) {
            if (written == buffer.Length) {
                bool atLimit = buffer.Length >= maxBytes;

                if (!atLimit && !sizeKnown) {
                    Array.Resize(ref buffer, NextCapacity(buffer.Length, maxBytes));
                } else {
                    // The buffer already holds the whole expected body or maxBytes bytes.
                    // One more byte shows whether the body continues.
                    byte[] probe = new byte[1];
                    int extra = await stream.ReadAsync(probe, cancellationToken).ConfigureAwait(false);

                    if (extra == 0) {
                        break;
                    }

                    total += extra;

                    if (atLimit) {
                        truncated = true;
                        break;
                    }

                    // The body is longer than its Content-Length: keep reading with a growing buffer.
                    sizeKnown = false;
                    Array.Resize(ref buffer, NextCapacity(buffer.Length, maxBytes));
                    buffer[written++] = probe[0];
                    continue;
                }
            }

            int read = await stream.ReadAsync(buffer.AsMemory(written), cancellationToken).ConfigureAwait(false);

            if (read == 0) {
                break;
            }

            written += read;
            total += read;
        }

        if (written == 0) {
            buffer = [];
        } else if (written < buffer.Length) {
            Array.Resize(ref buffer, written);
        }

        if (!truncated) {
            return new BoundedBody(buffer, false, total, true);
        }

        return lengthIsExact
            ? new BoundedBody(buffer, true, declaredLength!.Value, true)
            : new BoundedBody(buffer, true, total, false);
    }

    private static int NextCapacity(int current, int maxBytes) =>
        (int)Math.Min(Math.Max((long)current * 2, ChunkSize), maxBytes);

    /// <summary>
    /// Sends a request and reads the response body with a size limit.
    /// The response is read with <see cref="HttpCompletionOption.ResponseHeadersRead"/>, so the body is never buffered by the client.
    /// </summary>
    public static async Task<BoundedResponse> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        int maxBytes,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);

        HttpResponseMessage response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        return await ReadResponseAsync(response, maxBytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a GET request and reads the response body with a size limit.
    /// The response is read with <see cref="HttpCompletionOption.ResponseHeadersRead"/>, so the body is never buffered by the client.
    /// </summary>
    public static async Task<BoundedResponse> GetAsync(
        HttpClient http,
        string path,
        int maxBytes,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(http);

        HttpResponseMessage response = await http
            .GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        return await ReadResponseAsync(response, maxBytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BoundedResponse> ReadResponseAsync(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken cancellationToken
    ) {
        try {
            BoundedBody body = await ReadAsync(response.Content, maxBytes, cancellationToken).ConfigureAwait(false);

            return new BoundedResponse(response, body);
        } catch {
            response.Dispose();
            throw;
        }
    }
}
