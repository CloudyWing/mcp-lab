using System.Text.RegularExpressions;
using CloudyWing.McpLab.Shared;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class EnvironmentConnectionSettingsTests {
    private static readonly Regex EnvRegex = new(@"^TEST_CONN_([A-Z0-9]+)_([A-Z0-9_]+)$");

    [Test]
    public void LoadBuckets_MatchingVariables_ReturnsLowercaseFields() {
        using EnvironmentScope environmentScope = new("TEST_CONN_");
        Environment.SetEnvironmentVariable("TEST_CONN_MAIN_NAME", "main");
        Environment.SetEnvironmentVariable("TEST_CONN_MAIN_HOST", "localhost");

        Dictionary<string, Dictionary<string, string>> actual =
            EnvironmentConnectionSettings.LoadBuckets(EnvRegex);

        using (Assert.EnterMultipleScope()) {
            Assert.That(actual.ContainsKey("MAIN"), Is.True);
            Assert.That(actual["MAIN"]["name"], Is.EqualTo("main"));
            Assert.That(actual["MAIN"]["host"], Is.EqualTo("localhost"));
        }
    }

    [Test]
    public void GetRequiredString_MissingField_ThrowsInvalidOperationException() {
        Dictionary<string, string> cfg = new(StringComparer.OrdinalIgnoreCase) {
            ["name"] = "main",
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => EnvironmentConnectionSettings.GetRequiredString(cfg, "MAIN", "TEST_CONN", "host")
        )!;

        Assert.That(exception.Message, Does.Contain("TEST_CONN_MAIN_HOST is required"));
    }

    [Test]
    public void GetOptionalPort_AliasField_ReturnsConfiguredPort() {
        Dictionary<string, string> cfg = new(StringComparer.OrdinalIgnoreCase) {
            ["mgmt_port"] = "15673",
        };

        int actual = EnvironmentConnectionSettings.GetOptionalPort(
            cfg,
            "MAIN",
            "TEST_CONN",
            15672,
            "mgmtport",
            "mgmtport",
            "mgmt_port"
        );

        Assert.That(actual, Is.EqualTo(15673));
    }

    [Test]
    public void GetRequiredBool_InvalidValue_ThrowsInvalidOperationException() {
        Dictionary<string, string> cfg = new(StringComparer.OrdinalIgnoreCase) {
            ["ssl_skip_verify"] = "maybe",
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => EnvironmentConnectionSettings.GetRequiredBool(cfg, "MAIN", "TEST_CONN", "ssl_skip_verify")
        )!;

        Assert.That(exception.Message, Does.Contain("TEST_CONN_MAIN_SSL_SKIP_VERIFY must be true or false"));
    }

    private sealed class EnvironmentScope : IDisposable {
        private readonly string prefix;
        private readonly Dictionary<string, string?> originalValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentScope(string prefix) {
            this.prefix = prefix;

            foreach (string key in Environment.GetEnvironmentVariables().Keys.OfType<string>()) {
                if (key.StartsWith(prefix, StringComparison.Ordinal)) {
                    originalValues[key] = Environment.GetEnvironmentVariable(key);
                    Environment.SetEnvironmentVariable(key, null);
                }
            }
        }

        public void Dispose() {
            foreach (string key in Environment.GetEnvironmentVariables().Keys.OfType<string>()) {
                if (key.StartsWith(prefix, StringComparison.Ordinal)) {
                    Environment.SetEnvironmentVariable(key, null);
                }
            }

            foreach ((string key, string? value) in originalValues) {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
