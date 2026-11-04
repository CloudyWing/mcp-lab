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

    [TestCase("mcp-sql-server", 3)]
    [TestCase("mcp-api-contract", 3)]
    [TestCase("mcp-docker", 0)]
    public void ForService_KnownService_ReturnsItsSettings(string service, int expectedCount) {
        Assert.That(ServiceSettings.ForService(service), Has.Count.EqualTo(expectedCount));
    }

    [Test]
    public void ForService_AllSettings_HaveDefaultsWithinBounds() {
        string[] services = ["mcp-sql-server", "mcp-oracle", "mcp-elasticsearch", "mcp-redis", "mcp-api-contract", "mcp-oidc", "mcp-rabbitmq", "mcp-mailpit"];

        foreach (ServiceSetting setting in services.SelectMany(ServiceSettings.ForService)) {
            Assert.That(setting.DefaultValue, Is.InRange(setting.Min, setting.Max), setting.Name);
        }
    }
}
