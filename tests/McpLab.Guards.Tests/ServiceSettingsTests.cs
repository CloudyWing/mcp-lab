using CloudyWing.McpLab.Shared.Configuration;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ServiceSettingsTests {
    [TestCase(null, 15)]
    [TestCase("45", 45)]
    [TestCase("abc", 15)]
    [TestCase("0", 15)]
    [TestCase("9999", 300)]
    public void Read_HttpTimeout_AppliesDefaultAndBounds(string? configured, int expected) {
        Dictionary<string, string> snapshot = [];
        if (configured is not null) {
            snapshot["API_CONTRACT_HTTP_TIMEOUT"] = configured;
        }

        Assert.That(ServiceSettings.ApiContract.HttpTimeout.Read(snapshot), Is.EqualTo(expected));
    }

    [Test]
    public void Read_ServiceSpecificValueMissing_UsesFallbackVariable() {
        Dictionary<string, string> snapshot = new() {
            ["QUERY_TIMEOUT"] = "90",
        };

        Assert.That(ServiceSettings.SqlServer.QueryTimeout.Read(snapshot), Is.EqualTo(90));
    }

    [Test]
    public void Read_ServiceSpecificValuePresent_IgnoresFallbackVariable() {
        Dictionary<string, string> snapshot = new() {
            ["QUERY_TIMEOUT"] = "90",
            ["MSSQL_QUERY_TIMEOUT"] = "20",
        };

        Assert.That(ServiceSettings.SqlServer.QueryTimeout.Read(snapshot), Is.EqualTo(20));
    }

    [TestCase("mcp-sql-server", 4)]
    [TestCase("mcp-api-contract", 4)]
    [TestCase("mcp-mosquitto", 1)]
    [TestCase("mcp-docker", 1)]
    public void ForService_KnownService_ReturnsItsSettings(string service, int expectedCount) {
        Assert.That(ServiceSettings.ForService(service), Has.Count.EqualTo(expectedCount));
    }

    [Test]
    public void ForService_AllSettings_HaveDefaultsWithinBounds() {
        string[] services = ["mcp-sql-server", "mcp-oracle", "mcp-elasticsearch", "mcp-redis", "mcp-api-contract", "mcp-oidc", "mcp-rabbitmq", "mcp-mailpit", "mcp-mosquitto", "mcp-docker"];

        foreach (ServiceSetting setting in services.SelectMany(ServiceSettings.ForService)) {
            Assert.That(setting.DefaultValue, Is.InRange(setting.Min, setting.Max), setting.Name);
        }
    }

    [TestCase("mcp-sql-server")]
    [TestCase("mcp-oracle")]
    [TestCase("mcp-elasticsearch")]
    [TestCase("mcp-redis")]
    [TestCase("mcp-api-contract")]
    [TestCase("mcp-oidc")]
    [TestCase("mcp-rabbitmq")]
    [TestCase("mcp-mailpit")]
    [TestCase("mcp-mosquitto")]
    [TestCase("mcp-docker")]
    public void ForService_DotNetService_ContainsMaxResponseBytes(string service) {
        Assert.That(ServiceSettings.ForService(service), Does.Contain(ServiceSettings.Common.MaxResponseBytes));
    }

    [Test]
    public void MaxResponseBytes_Definition_HasDocumentedNameDefaultAndBounds() {
        ServiceSetting setting = ServiceSettings.Common.MaxResponseBytes;

        using (Assert.EnterMultipleScope()) {
            Assert.That(setting.Name, Is.EqualTo("MCPLAB_MAX_RESPONSE_BYTES"));
            Assert.That(setting.DefaultValue, Is.EqualTo(1_048_576));
            Assert.That(setting.Min, Is.EqualTo(1));
            Assert.That(setting.Max, Is.EqualTo(2_097_152));
        }
    }

    [TestCase(null, 1_048_576)]
    [TestCase("2048", 2048)]
    [TestCase("abc", 1_048_576)]
    [TestCase("0", 1_048_576)]
    [TestCase("2097152", 2_097_152)]
    [TestCase("2097153", 2_097_152)]
    [TestCase("999999999", 2_097_152)]
    public void Read_MaxResponseBytes_AppliesDefaultAndBounds(string? configured, int expected) {
        Dictionary<string, string> snapshot = [];
        if (configured is not null) {
            snapshot["MCPLAB_MAX_RESPONSE_BYTES"] = configured;
        }

        Assert.That(ServiceSettings.Common.MaxResponseBytes.Read(snapshot), Is.EqualTo(expected));
    }

    [Test]
    public void Read_MaxResponseBytes_SnapshotChanged_ReturnsNewValue() {
        Dictionary<string, string> snapshot = new() {
            ["MCPLAB_MAX_RESPONSE_BYTES"] = "4096",
        };
        int before = ServiceSettings.Common.MaxResponseBytes.Read(snapshot);

        snapshot["MCPLAB_MAX_RESPONSE_BYTES"] = "512";
        int after = ServiceSettings.Common.MaxResponseBytes.Read(snapshot);

        Assert.That((before, after), Is.EqualTo((4096, 512)));
    }
}
