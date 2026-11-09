using System.Text;

namespace CloudyWing.McpLab.Shared;

/// <summary>
/// Decodes UTF-8 bytes that may have been cut in the middle of a character.
/// </summary>
public static class Utf8Text {
    private const int MaxSequenceLength = 4;

    /// <summary>
    /// Decodes UTF-8 bytes, dropping a trailing incomplete sequence when the bytes were truncated.
    /// </summary>
    /// <param name="bytes">UTF-8 bytes.</param>
    /// <param name="truncated">Whether <paramref name="bytes"/> is a prefix of a longer content.</param>
    public static string DecodePrefix(ReadOnlySpan<byte> bytes, bool truncated) {
        if (!truncated) {
            return Encoding.UTF8.GetString(bytes);
        }

        return Encoding.UTF8.GetString(bytes[..CompleteLength(bytes)]);
    }

    private static int CompleteLength(ReadOnlySpan<byte> bytes) {
        int lowest = Math.Max(0, bytes.Length - MaxSequenceLength);

        for (int i = bytes.Length - 1; i >= lowest; i--) {
            byte value = bytes[i];

            // Continuation bytes (10xxxxxx) belong to a lead byte located earlier.
            if ((value & 0b1100_0000) == 0b1000_0000) {
                continue;
            }

            int expected = value switch {
                < 0b1000_0000 => 1,
                >= 0b1111_0000 and < 0b1111_1000 => 4,
                >= 0b1110_0000 and < 0b1111_0000 => 3,
                >= 0b1100_0000 and < 0b1110_0000 => 2,
                _ => 1
            };

            return i + expected > bytes.Length ? i : bytes.Length;
        }

        return bytes.Length;
    }
}
