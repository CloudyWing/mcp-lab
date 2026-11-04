namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Reads HTTP response bodies without buffering more than a fixed number of bytes.
/// </summary>
public static class BoundedHttpContent {
    /// <summary>
    /// Reads up to <paramref name="maxBytes"/> bytes and reports whether the body was longer.
    /// </summary>
    public static async Task<(byte[] Bytes, bool Truncated)> ReadAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[maxBytes + 1];
        int total = 0;

        while (total < buffer.Length) {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);

            if (read == 0) {
                break;
            }

            total += read;
        }

        bool truncated = total > maxBytes;

        return (buffer.AsSpan(0, Math.Min(total, maxBytes)).ToArray(), truncated);
    }
}
