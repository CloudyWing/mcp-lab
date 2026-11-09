namespace CloudyWing.McpLab.Shared;

/// <summary>
/// A response body read with a size limit.
/// </summary>
/// <param name="Bytes">The bytes that were read, at most the requested limit.</param>
/// <param name="Truncated">Whether the body is longer than <paramref name="Bytes"/>.</param>
/// <param name="OriginalBytes">The size of the body, or the bytes read before reading stopped.</param>
/// <param name="OriginalBytesExact">Whether <paramref name="OriginalBytes"/> is the exact size.</param>
public readonly record struct BoundedBody(byte[] Bytes, bool Truncated, long OriginalBytes, bool OriginalBytesExact) {
    /// <summary>
    /// Decodes the body as UTF-8, dropping a trailing incomplete sequence when the body is truncated.
    /// </summary>
    public string ToText() => Utf8Text.DecodePrefix(Bytes, Truncated);

    /// <summary>
    /// Decodes the body as UTF-8 and throws when it was truncated.
    /// </summary>
    /// <param name="maxBytes">The limit that was applied while reading.</param>
    /// <exception cref="ResponseTooLargeException">The body is truncated.</exception>
    public string RequireComplete(int maxBytes) {
        if (Truncated) {
            throw new ResponseTooLargeException(OriginalBytes, OriginalBytesExact, maxBytes);
        }

        return ToText();
    }
}
