using System.Text;
using System.Text.Json;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Accumulates the bytes returned by a single tool call so that readers can stop once the response limit is reached.
/// This type is not thread-safe; callers that share it across threads must synchronize access.
/// </summary>
public sealed class ResponseBudget {
    private long usedBytes;
    private long observedBytes;

    /// <summary>
    /// Initializes a new instance of <see cref="ResponseBudget"/>.
    /// </summary>
    /// <param name="maxBytes">Largest number of bytes that can be accepted.</param>
    public ResponseBudget(int maxBytes) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        MaxBytes = maxBytes;
    }

    /// <summary>
    /// Gets the largest number of bytes that can be accepted.
    /// </summary>
    public int MaxBytes { get; }

    /// <summary>
    /// Gets the number of bytes accepted so far.
    /// </summary>
    public long UsedBytes => usedBytes;

    /// <summary>
    /// Gets the accepted bytes plus the bytes that were rejected or cut off.
    /// </summary>
    public long ObservedBytes => observedBytes;

    /// <summary>
    /// Gets the number of bytes that can still be accepted.
    /// </summary>
    public long Remaining => MaxBytes - usedBytes;

    /// <summary>
    /// Gets a value indicating whether any offer was rejected or cut off.
    /// </summary>
    public bool Exhausted { get; private set; }

    /// <summary>
    /// Accepts all of <paramref name="bytes"/> or none of them.
    /// </summary>
    /// <param name="bytes">Size of the item being offered.</param>
    /// <returns><see langword="true"/> when the item fits in the remaining budget; otherwise <see langword="false"/>.</returns>
    public bool TryAdd(long bytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        observedBytes += bytes;

        if (bytes > Remaining) {
            Exhausted = true;
            return false;
        }

        usedBytes += bytes;
        return true;
    }

    /// <summary>
    /// Accepts as many of <paramref name="bytes"/> as the remaining budget allows.
    /// </summary>
    /// <param name="bytes">Size of the item being offered.</param>
    /// <returns>The number of bytes accepted.</returns>
    public long TakeUpTo(long bytes) {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        long accepted = Math.Min(bytes, Remaining);
        usedBytes += accepted;
        observedBytes += bytes;

        if (accepted < bytes) {
            Exhausted = true;
        }

        return accepted;
    }

    /// <summary>
    /// Accepts an item whose size is its UTF-8 JSON length, or none of it.
    /// </summary>
    /// <param name="item">Item to serialize.</param>
    /// <returns><see langword="true"/> when the item fits in the remaining budget; otherwise <see langword="false"/>.</returns>
    public bool TryAddJson<T>(T item) => TryAdd(JsonSerializer.SerializeToUtf8Bytes(item, ToolJson.Options).Length);

    /// <summary>
    /// Gets the UTF-8 byte count of a string.
    /// </summary>
    public static int Utf8Size(string? text) => string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);
}
