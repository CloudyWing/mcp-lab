using System.Text.Json.Nodes;

using NSubstitute;
using NUnit.Framework;

using ApiContractConnectionRegistry = CloudyWing.McpLab.ApiContract.ConnectionRegistry;
using ApiContractSpecSourceClient = CloudyWing.McpLab.ApiContract.SpecSourceClient;
using ApiContractTools = CloudyWing.McpLab.ApiContract.ApiContractTools;
using ElasticConnectionRegistry = CloudyWing.McpLab.Elastic.ConnectionRegistry;
using MailpitConnectionRegistry = CloudyWing.McpLab.Mailpit.ConnectionRegistry;
using MosquittoConnectionRegistry = CloudyWing.McpLab.Mosquitto.ConnectionRegistry;
using OidcConnectionRegistry = CloudyWing.McpLab.Oidc.ConnectionRegistry;
using OracleConnectionRegistry = CloudyWing.McpLab.Oracle.ConnectionRegistry;
using RabbitMqConnectionRegistry = CloudyWing.McpLab.RabbitMq.ConnectionRegistry;
using RedisConnectionRegistry = CloudyWing.McpLab.Redis.ConnectionRegistry;
using SqlServerConnectionRegistry = CloudyWing.McpLab.SqlServer.ConnectionRegistry;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ConnectionRegistryPlaceholderTests {
    private static readonly string[] EnvironmentPrefixes = [
        "API_CONTRACT_CONN_",
        "MSSQL_CONN_",
        "ORACLE_CONN_",
        "ES_CONN_",
        "REDIS_CONN_",
        "OIDC_CONN_",
        "MAILPIT_CONN_",
        "MQTT_CONN_",
        "RABBITMQ_CONN_",
    ];

    [Test]
    public void SqlServerConnectionRegistry_BlankTemplateVariables_IgnoresConnection() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("MSSQL_CONN_TEMPLATE", [
            ("NAME", ""),
            ("HOST", ""),
            ("PORT", "1433"),
            ("USER", ""),
            ("PASSWORD", ""),
            ("DATABASE", ""),
        ]);

        SqlServerConnectionRegistry registry = new();

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void ApiContractConnectionRegistry_BlankTemplateVariables_IgnoresConnection() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("API_CONTRACT_CONN_TEMPLATE", [
            ("NAME", ""),
            ("SPEC_URL", ""),
            ("SPEC_PATH", ""),
            ("BASE_URL", ""),
            ("ALLOWED_METHODS", ""),
            ("INVOKE_ENABLED", "false"),
        ]);
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();

        ApiContractConnectionRegistry registry = new(httpClientFactory);

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void ApiContractConnectionRegistry_InvokeEnabledVariable_LoadsConfiguredValue() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("API_CONTRACT_CONN_STAGE", [
            ("NAME", "stage"),
            ("SPEC_URL", "https://api.example.test/openapi.json"),
            ("INVOKE_ENABLED", "true"),
        ]);
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();

        ApiContractConnectionRegistry registry = new(httpClientFactory);

        Assert.That(registry.All["stage"].InvokeEnabled, Is.True);
    }

    [Test]
    public async Task ApiContractTools_InvokeEndpoint_WhenInvokeDisabled_ReturnsBlocked() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("API_CONTRACT_CONN_STAGE", [
            ("NAME", "stage"),
            ("SPEC_URL", "https://api.example.test/openapi.json"),
        ]);
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        ApiContractConnectionRegistry registry = new(httpClientFactory);
        ApiContractTools tools = new(registry, new ApiContractSpecSourceClient(registry));

        string result = await tools.InvokeEndpoint("/users").ConfigureAwait(false);
        JsonNode? json = JsonNode.Parse(result);

        using (Assert.EnterMultipleScope()) {
            Assert.That(json?["ok"]?.GetValue<bool>(), Is.False);
            Assert.That(json?["kind"]?.GetValue<string>(), Is.EqualTo("blocked"));
            Assert.That(json?["message"]?.GetValue<string>(), Does.Contain("invoke_endpoint is disabled"));
        }
    }

    [Test]
    public void OracleConnectionRegistry_BlankTemplateVariables_IgnoresConnection() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("ORACLE_CONN_TEMPLATE", [
            ("NAME", ""),
            ("HOST", ""),
            ("PORT", "1521"),
            ("SERVICE", ""),
            ("USER", ""),
            ("PASSWORD", ""),
        ]);

        OracleConnectionRegistry registry = new();

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void ElasticsearchConnectionRegistry_BlankTemplateVariables_IgnoresConnection() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("ES_CONN_TEMPLATE", [
            ("NAME", ""),
            ("URL", ""),
            ("USER", ""),
            ("PASSWORD", ""),
            ("SSL_SKIP_VERIFY", ""),
        ]);
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();

        ElasticConnectionRegistry registry = new(httpClientFactory);

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void RedisConnectionRegistry_BlankTemplateVariables_IgnoresConnection() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("REDIS_CONN_TEMPLATE", [
            ("NAME", ""),
            ("HOST", ""),
            ("PORT", "6379"),
            ("DATABASE", ""),
            ("PASSWORD", ""),
        ]);

        RedisConnectionRegistry registry = new();

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void OidcConnectionRegistry_BlankTemplateVariables_IgnoresConnection() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("OIDC_CONN_TEMPLATE", [
            ("NAME", ""),
            ("ISSUER", ""),
            ("DISCOVERY_URL", ""),
            ("AUDIENCE", ""),
            ("REQUIRE_HTTPS_METADATA", ""),
        ]);

        OidcConnectionRegistry registry = new();

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void MosquittoConnectionRegistry_BlankTemplateVariables_IgnoresConnection() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("MQTT_CONN_TEMPLATE", [
            ("NAME", ""),
            ("HOST", ""),
            ("PORT", "1883"),
            ("USER", ""),
            ("PASSWORD", ""),
        ]);

        MosquittoConnectionRegistry registry = new();

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void RabbitMqConnectionRegistry_BlankTemplateVariables_IgnoresConnection() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("RABBITMQ_CONN_TEMPLATE", [
            ("NAME", ""),
            ("HOST", ""),
            ("MGMTPORT", "15672"),
            ("USER", ""),
            ("PASSWORD", ""),
        ]);
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();

        RabbitMqConnectionRegistry registry = new(httpClientFactory);

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void MailpitConnectionRegistry_BlankTemplateVariables_IgnoresConnection() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("MAILPIT_CONN_TEMPLATE", [
            ("NAME", ""),
            ("URL", ""),
        ]);
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();

        MailpitConnectionRegistry registry = new(httpClientFactory);

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void RabbitMqConnectionRegistry_MgmtPortUnderscoreVariable_LoadsConfiguredPort() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("RABBITMQ_CONN_MAIN", [
            ("NAME", "main"),
            ("HOST", "localhost"),
            ("MGMT_PORT", "15673"),
            ("USER", "guest"),
            ("PASSWORD", "guest"),
        ]);
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();

        RabbitMqConnectionRegistry registry = new(httpClientFactory);

        Assert.That(registry.All["main"].MgmtPort, Is.EqualTo(15673));
    }

    [Test]
    public void SqlServerConnectionRegistry_PartialVariables_ThrowsInvalidOperationException() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("MSSQL_CONN_PARTIAL", [
            ("NAME", "partial"),
            ("PORT", "1433"),
        ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new SqlServerConnectionRegistry()
        )!;

        Assert.That(exception.Message, Does.Contain("MSSQL_CONN_PARTIAL_HOST is required"));
    }

    [Test]
    public void OidcConnectionRegistry_PartialVariables_ThrowsInvalidOperationException() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("OIDC_CONN_PARTIAL", [
            ("NAME", "partial"),
            ("AUDIENCE", "api://default"),
        ]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new OidcConnectionRegistry()
        )!;

        Assert.That(exception.Message, Does.Contain("OIDC_CONN_PARTIAL_ISSUER is required"));
    }

    [Test]
    public void ApiContractConnectionRegistry_PartialVariables_ThrowsInvalidOperationException() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("API_CONTRACT_CONN_PARTIAL", [
            ("NAME", "partial"),
        ]);
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new ApiContractConnectionRegistry(httpClientFactory)
        )!;

        Assert.That(exception.Message, Does.Contain("API_CONTRACT_CONN_PARTIAL_SPEC_URL or SPEC_PATH is required"));
    }

    private static void SetEnvironmentVariables(string prefix, IEnumerable<(string Field, string Value)> entries) {
        foreach ((string field, string value) in entries) {
            Environment.SetEnvironmentVariable($"{prefix}_{field}", value);
        }
    }

    private sealed class EnvironmentScope : IDisposable {
        private readonly Dictionary<string, string?> originalValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentScope() {
            foreach (string key in Environment.GetEnvironmentVariables().Keys.OfType<string>()) {
                if (EnvironmentPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))) {
                    originalValues[key] = Environment.GetEnvironmentVariable(key);
                    Environment.SetEnvironmentVariable(key, null);
                }
            }
        }

        public void Dispose() {
            foreach (string key in Environment.GetEnvironmentVariables().Keys.OfType<string>()) {
                if (EnvironmentPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))) {
                    Environment.SetEnvironmentVariable(key, null);
                }
            }

            foreach ((string key, string? value) in originalValues) {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
