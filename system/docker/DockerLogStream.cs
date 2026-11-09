namespace CloudyWing.McpLab.Docker;

/// <summary>
/// Decodes Docker log streams returned by the Engine API.
/// </summary>
public static class DockerLogStream {
    private const int HeaderLength = 8;

    /// <summary>
    /// Decodes either raw TTY logs or Docker multiplexed stdout/stderr logs.
    /// </summary>
    public static string Decode(byte[] bytes) => Decode(bytes, false);

    /// <summary>
    /// Decodes either raw TTY logs or Docker multiplexed stdout/stderr logs.
    /// </summary>
    /// <param name="bytes">The log bytes.</param>
    /// <param name="partial">
    /// Whether <paramref name="bytes"/> is a prefix of a longer stream. A multiplexed frame that ends
    /// before its declared length is decoded up to the available bytes, and an incomplete header is ignored.
    /// </param>
    public static string Decode(byte[] bytes, bool partial) {
        if (bytes.Length < HeaderLength) {
            return partial && IsHeaderPrefix(bytes) ? "" : DecodeRaw(bytes, partial);
        }

        if (!LooksLikeMultiplexedFrame(bytes, partial)) {
            return DecodeRaw(bytes, partial);
        }

        StringBuilder output = new();
        int offset = 0;

        while (offset + HeaderLength <= bytes.Length) {
            int streamType = bytes[offset];
            int length = ReadLength(bytes, offset);
            int available = bytes.Length - offset - HeaderLength;

            if (streamType is < 0 or > 2 || length < 0) {
                return DecodeRaw(bytes, partial);
            }

            if (length > available) {
                if (!partial) {
                    return DecodeRaw(bytes, partial);
                }

                output.Append(Utf8Text.DecodePrefix(bytes.AsSpan(offset + HeaderLength, available), true));

                return output.ToString();
            }

            output.Append(Encoding.UTF8.GetString(bytes, offset + HeaderLength, length));
            offset += HeaderLength + length;
        }

        if (offset != bytes.Length && !partial) {
            return DecodeRaw(bytes, partial);
        }

        return output.ToString();
    }

    private static string DecodeRaw(byte[] bytes, bool partial) => Utf8Text.DecodePrefix(bytes, partial);

    private static int ReadLength(byte[] bytes, int offset) =>
        (bytes[offset + 4] << 24)
        | (bytes[offset + 5] << 16)
        | (bytes[offset + 6] << 8)
        | bytes[offset + 7];

    private static bool LooksLikeMultiplexedFrame(byte[] bytes, bool partial) {
        int streamType = bytes[0];
        int length = ReadLength(bytes, 0);

        return streamType is >= 0 and <= 2
            && bytes[1] == 0
            && bytes[2] == 0
            && bytes[3] == 0
            && length >= 0
            && (partial || length <= bytes.Length - HeaderLength);
    }

    // A prefix shorter than one header is treated as an incomplete frame header when every byte
    // seen so far matches the fixed part of a header (stream type 0 to 2 followed by three zero bytes).
    private static bool IsHeaderPrefix(byte[] bytes) {
        if (bytes.Length == 0 || bytes[0] > 2) {
            return false;
        }

        for (int i = 1; i < Math.Min(bytes.Length, 4); i++) {
            if (bytes[i] != 0) {
                return false;
            }
        }

        return true;
    }
}
