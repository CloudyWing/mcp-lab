using System.Security.Cryptography;
using System.Text;

namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// Checks the single admin password set by <c>MCPLAB_ADMIN_PASSWORD</c>.
/// </summary>
public sealed class AdminPassword {
    /// <summary>
    /// Environment variable holding the password.
    /// </summary>
    public const string VariableName = "MCPLAB_ADMIN_PASSWORD";

    /// <summary>
    /// Shortest accepted password.
    /// </summary>
    public const int MinimumLength = 8;

    private readonly byte[] expected;

    /// <summary>
    /// Initializes a new instance of <see cref="AdminPassword"/>.
    /// </summary>
    public AdminPassword(string? password) {
        string value = password?.Trim() ?? "";
        IsConfigured = value.Length >= MinimumLength;
        expected = Encoding.UTF8.GetBytes(value);
    }

    /// <summary>
    /// Gets whether a usable password is configured. Sign-in is refused until it is.
    /// </summary>
    public bool IsConfigured { get; }

    /// <summary>
    /// Compares a password in constant time.
    /// </summary>
    public bool Verify(string? candidate) =>
        IsConfigured && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate ?? ""), expected);
}
