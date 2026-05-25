namespace CloudyWing.McpLab.Shared.DotEnv;

/// <summary>
/// Describes the observable state of a .env file.
/// </summary>
/// <param name="Exists">A value indicating whether the file exists.</param>
/// <param name="LastWriteTimeUtc">The file last write time in UTC.</param>
/// <param name="Length">The file length in bytes.</param>
public sealed record DotEnvFileFingerprint(bool Exists, DateTime LastWriteTimeUtc, long Length);
