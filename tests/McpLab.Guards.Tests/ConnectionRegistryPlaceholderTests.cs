using NSubstitute;
using NUnit.Framework;

using ElasticConnectionRegistry = CloudyWing.McpLab.Elastic.ConnectionRegistry;
using MosquittoConnectionRegistry = CloudyWing.McpLab.Mosquitto.ConnectionRegistry;
using OracleConnectionRegistry = CloudyWing.McpLab.Oracle.ConnectionRegistry;
using RabbitMqConnectionRegistry = CloudyWing.McpLab.RabbitMq.ConnectionRegistry;
using RedisConnectionRegistry = CloudyWing.McpLab.Redis.ConnectionRegistry;
using SqlServerConnectionRegistry = CloudyWing.McpLab.SqlServer.ConnectionRegistry;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ConnectionRegistryPlaceholderTests {
    private static readonly string[] EnvironmentPrefixes = [
        "MSSQL_CONN_",
        "ORACLE_CONN_",
        "ES_CONN_",
        "REDIS_CONN_",
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
