using System.Text;

namespace CloudyWing.McpLab.Shared.DotEnv;

/// <summary>
/// Reads a .env file from disk.
/// </summary>
public sealed class DotEnvFileSource {
    private static readonly UTF8Encoding StrictUtf8Encoding = new(false, true);

    /// <summary>
    /// Initializes a new instance of <see cref="DotEnvFileSource"/>.
    /// </summary>
    /// <param name="path">The .env file path.</param>
    public DotEnvFileSource(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>
    /// Gets the .env file path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Reads the current .env file content.
    /// </summary>
    /// <returns>The file content, or an empty string when the file does not exist.</returns>
    public string ReadAllText() {
        if (!File.Exists(Path)) {
            return "";
        }

        return File.ReadAllText(Path, StrictUtf8Encoding);
    }

    /// <summary>
    /// Gets a fingerprint that changes when the source file changes.
    /// </summary>
    /// <returns>The file fingerprint.</returns>
    public DotEnvFileFingerprint GetFingerprint() {
        FileInfo fileInfo = new(Path);

        return fileInfo.Exists
            ? new DotEnvFileFingerprint(true, fileInfo.LastWriteTimeUtc, fileInfo.Length)
            : new DotEnvFileFingerprint(false, DateTime.MinValue, 0);
    }
}
