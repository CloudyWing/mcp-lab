using CloudyWing.McpLab.Shared.Configuration;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// The exception thrown when a response that cannot be returned partially is larger than the response limit.
/// </summary>
public sealed class ResponseTooLargeException : InvalidOperationException {
    /// <summary>
    /// Initializes a new instance of <see cref="ResponseTooLargeException"/>.
    /// </summary>
    /// <param name="originalBytes">Size of the response, or the bytes read before reading stopped.</param>
    /// <param name="originalBytesExact">Whether <paramref name="originalBytes"/> is the exact size.</param>
    /// <param name="maxResponseBytes">The effective response limit.</param>
    public ResponseTooLargeException(long originalBytes, bool originalBytesExact, int maxResponseBytes)
        : base(BuildMessage(originalBytes, originalBytesExact, maxResponseBytes)) {
        OriginalBytes = originalBytes;
        OriginalBytesExact = originalBytesExact;
        MaxResponseBytes = maxResponseBytes;
    }

    /// <summary>
    /// Gets the size of the response, or the bytes read before reading stopped.
    /// </summary>
    public long OriginalBytes { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="OriginalBytes"/> is the exact size.
    /// </summary>
    public bool OriginalBytesExact { get; }

    /// <summary>
    /// Gets the effective response limit.
    /// </summary>
    public int MaxResponseBytes { get; }

    private static string BuildMessage(long originalBytes, bool originalBytesExact, int maxResponseBytes) {
        string size = originalBytesExact
            ? $"{originalBytes} bytes"
            : $"at least {originalBytes} bytes";

        return $"The response ({size}) exceeds the limit of {maxResponseBytes} bytes and cannot be returned partially. "
            + $"Increase {ServiceSettings.Common.MaxResponseBytes.Name} or narrow the request.";
    }
}
