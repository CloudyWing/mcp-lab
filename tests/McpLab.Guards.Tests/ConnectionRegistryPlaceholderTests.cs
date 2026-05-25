using System.Text.Json.Nodes;

using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;
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

        SqlServerConnectionRegistry registry = CreateSqlServerRegistryFromEnvironment();

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

        ApiContractConnectionRegistry registry = CreateApiContractRegistry(httpClientFactory);

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

        ApiContractConnectionRegistry registry = CreateApiContractRegistry(httpClientFactory);

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
        ApiContractConnectionRegistry registry = CreateApiContractRegistry(httpClientFactory);
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

        OracleConnectionRegistry registry = CreateOracleRegistry();

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

        ElasticConnectionRegistry registry = CreateElasticRegistry(httpClientFactory);

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

        RedisConnectionRegistry registry = CreateRedisRegistry();

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

        OidcConnectionRegistry registry = CreateOidcRegistry();

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

        MosquittoConnectionRegistry registry = CreateMosquittoRegistry();

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

        RabbitMqConnectionRegistry registry = CreateRabbitMqRegistry(httpClientFactory);

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

        MailpitConnectionRegistry registry = CreateMailpitRegistry(httpClientFactory);

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

        RabbitMqConnectionRegistry registry = CreateRabbitMqRegistry(httpClientFactory);

        Assert.That(registry.All["main"].MgmtPort, Is.EqualTo(15673));
    }

    [Test]
    public void SqlServerConnectionRegistry_PartialVariables_IgnoresConnectionWhenNoPreviousConfig() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("MSSQL_CONN_PARTIAL", [
            ("NAME", "partial"),
            ("PORT", "1433"),
        ]);

        SqlServerConnectionRegistry registry = CreateSqlServerRegistryFromEnvironment();

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void OidcConnectionRegistry_PartialVariables_IgnoresConnectionWhenNoPreviousConfig() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("OIDC_CONN_PARTIAL", [
            ("NAME", "partial"),
            ("AUDIENCE", "api://default"),
        ]);

        OidcConnectionRegistry registry = CreateOidcRegistry();

        Assert.That(registry.All, Is.Empty);
    }

    [Test]
    public void ApiContractConnectionRegistry_PartialVariables_IgnoresConnectionWhenNoPreviousConfig() {
        using EnvironmentScope environmentScope = new();
        SetEnvironmentVariables("API_CONTRACT_CONN_PARTIAL", [
            ("NAME", "partial"),
        ]);
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();

        ApiContractConnectionRegistry registry = CreateApiContractRegistry(httpClientFactory);

        Assert.That(registry.All, Is.Empty);
    }

    private static void SetEnvironmentVariables(string prefix, IEnumerable<(string Field, string Value)> entries) {
        foreach ((string field, string value) in entries) {
            Environment.SetEnvironmentVariable($"{prefix}_{field}", value);
        }
    }

    private static SqlServerConnectionRegistry CreateSqlServerRegistryFromEnvironment() =>
        new(
            new TestDotEnvSnapshotProvider(CaptureEnvironment()),
            Substitute.For<ILogger<SqlServerConnectionRegistry>>()
        );

    private static ApiContractConnectionRegistry CreateApiContractRegistry(IHttpClientFactory httpClientFactory) =>
        new(
            httpClientFactory,
            new TestDotEnvSnapshotProvider(CaptureEnvironment()),
            Substitute.For<ILogger<ApiContractConnectionRegistry>>()
        );

    private static ElasticConnectionRegistry CreateElasticRegistry(IHttpClientFactory httpClientFactory) =>
        new(
            httpClientFactory,
            new TestDotEnvSnapshotProvider(CaptureEnvironment()),
            Substitute.For<ILogger<ElasticConnectionRegistry>>()
        );

    private static MailpitConnectionRegistry CreateMailpitRegistry(IHttpClientFactory httpClientFactory) =>
        new(
            httpClientFactory,
            new TestDotEnvSnapshotProvider(CaptureEnvironment()),
            Substitute.For<ILogger<MailpitConnectionRegistry>>()
        );

    private static RabbitMqConnectionRegistry CreateRabbitMqRegistry(IHttpClientFactory httpClientFactory) =>
        new(
            httpClientFactory,
            new TestDotEnvSnapshotProvider(CaptureEnvironment()),
            Substitute.For<ILogger<RabbitMqConnectionRegistry>>()
        );

    private static MosquittoConnectionRegistry CreateMosquittoRegistry() =>
        new(
            new TestDotEnvSnapshotProvider(CaptureEnvironment()),
            Substitute.For<ILogger<MosquittoConnectionRegistry>>()
        );

    private static OidcConnectionRegistry CreateOidcRegistry() =>
        new(
            new TestDotEnvSnapshotProvider(CaptureEnvironment()),
            Substitute.For<ILogger<OidcConnectionRegistry>>()
        );

    private static OracleConnectionRegistry CreateOracleRegistry() =>
        new(
            new TestDotEnvSnapshotProvider(CaptureEnvironment()),
            Substitute.For<ILogger<OracleConnectionRegistry>>()
        );

    private static RedisConnectionRegistry CreateRedisRegistry() =>
        new(
            new TestDotEnvSnapshotProvider(CaptureEnvironment()),
            Substitute.For<ILogger<RedisConnectionRegistry>>()
        );

    private static IReadOnlyDictionary<string, string> CaptureEnvironment() {
        Dictionary<string, string> snapshot = new(StringComparer.Ordinal);

        foreach (string key in Environment.GetEnvironmentVariables().Keys.OfType<string>()) {
            snapshot[key] = Environment.GetEnvironmentVariable(key) ?? "";
        }

        return snapshot;
    }

    private sealed class TestDotEnvSnapshotProvider : IDotEnvSnapshotProvider {
        public TestDotEnvSnapshotProvider(IReadOnlyDictionary<string, string> current) {
            Current = current;
        }

        public IReadOnlyDictionary<string, string> Current { get; }

        public event EventHandler<DotEnvReloadedEventArgs>? Reloaded {
            add {
            }
            remove {
            }
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
