namespace CloudyWing.McpLab.Docker;

/// <summary>
/// Decodes Docker log streams returned by the Engine API.
/// </summary>
public static class DockerLogStream {
    /// <summary>
    /// Decodes either raw TTY logs or Docker multiplexed stdout/stderr logs.
    /// </summary>
    public static string Decode(byte[] bytes) {
        if (bytes.Length < 8 || !LooksLikeMultiplexedFrame(bytes)) {
            return Encoding.UTF8.GetString(bytes);
        }

        StringBuilder output = new();
        int offset = 0;

        while (offset + 8 <= bytes.Length) {
            int streamType = bytes[offset];
            int length = (bytes[offset + 4] << 24)
                | (bytes[offset + 5] << 16)
                | (bytes[offset + 6] << 8)
                | bytes[offset + 7];

            if (streamType is < 0 or > 2 || length < 0 || offset + 8 + length > bytes.Length) {
                return Encoding.UTF8.GetString(bytes);
            }

            output.Append(Encoding.UTF8.GetString(bytes, offset + 8, length));
            offset += 8 + length;
        }

        if (offset != bytes.Length) {
            return Encoding.UTF8.GetString(bytes);
        }

        return output.ToString();
    }

    private static bool LooksLikeMultiplexedFrame(byte[] bytes) {
        int streamType = bytes[0];
        int length = (bytes[4] << 24)
            | (bytes[5] << 16)
            | (bytes[6] << 8)
            | bytes[7];

        return streamType is >= 0 and <= 2
            && bytes[1] == 0
            && bytes[2] == 0
            && bytes[3] == 0
            && length >= 0
            && length <= bytes.Length - 8;
    }
}
