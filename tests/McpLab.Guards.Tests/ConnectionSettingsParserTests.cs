using CloudyWing.McpLab.Shared.Configuration;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class ConnectionSettingsParserTests {
    [Test]
    public void Parse_MinimalSqlServer_AppliesDefaultsAndDerivesName() {
        ConnectionParseResult result = Parse(ConnectionSchemas.SqlServer, new() {
            ["MSSQL_CONN_PROD_HOST"] = "db",
            ["MSSQL_CONN_PROD_USER"] = "sa",
            ["MSSQL_CONN_PROD_PASSWORD"] = "pw",
        });

        ConnectionValues values = result.Connections.Single();
        Assert.Multiple(() => {
            Assert.That(values.Name, Is.EqualTo("prod"));
            Assert.That(values.GetInt("PORT"), Is.EqualTo(1433));
            Assert.That(values.GetString("DATABASE"), Is.EqualTo("master"));
            Assert.That(values.GetBool("TRUST_SERVER_CERTIFICATE"), Is.True);
            Assert.That(values.GetInt("CONNECT_TIMEOUT"), Is.EqualTo(30));
            Assert.That(result.Rejected, Is.Empty);
        });
    }

    [Test]
    public void Parse_RedisWithoutPassword_LoadsWithDefaultDatabase() {
        ConnectionParseResult result = Parse(ConnectionSchemas.Redis, new() {
            ["REDIS_CONN_LOCAL_HOST"] = "redis",
        });

        ConnectionValues values = result.Connections.Single();
        Assert.Multiple(() => {
            Assert.That(values.GetString("PASSWORD"), Is.Empty);
            Assert.That(values.GetInt("DATABASE"), Is.Zero);
        });
    }

    [TestCase("0")]
    [TestCase("-5")]
    public void Parse_NonPositiveTimeout_RejectsWithReason(string timeout) {
        ConnectionParseResult result = Parse(ConnectionSchemas.Redis, new() {
            ["REDIS_CONN_LAB_HOST"] = "redis",
            ["REDIS_CONN_LAB_OPERATION_TIMEOUT"] = timeout,
        });

        Assert.That(result.Rejected.Single().Errors.Single(), Does.Contain("REDIS_CONN_LAB_OPERATION_TIMEOUT must be an integer of 1 or greater"));
    }

    [Test]
    public void Parse_MessagingConnections_DefaultToPlainTextWithCertificateValidation() {
        ConnectionParseResult mqtt = Parse(ConnectionSchemas.Mqtt, new() { ["MQTT_CONN_A_HOST"] = "mqtt" });
        ConnectionParseResult rabbit = Parse(ConnectionSchemas.RabbitMq, new() {
            ["RABBITMQ_CONN_A_HOST"] = "rabbit",
            ["RABBITMQ_CONN_A_USER"] = "u",
            ["RABBITMQ_CONN_A_PASSWORD"] = "p",
            ["RABBITMQ_CONN_A_USE_TLS"] = "true",
        });

        Assert.Multiple(() => {
            Assert.That(mqtt.Connections.Single().GetBool("USE_TLS"), Is.False);
            Assert.That(rabbit.Connections.Single().GetBool("USE_TLS"), Is.True);
            Assert.That(rabbit.Connections.Single().GetBool("SSL_SKIP_VERIFY"), Is.False);
        });
    }

    [Test]
    public void Parse_ElasticsearchWithoutCredentials_LoadsWithSslVerification() {
        ConnectionParseResult result = Parse(ConnectionSchemas.Elasticsearch, new() {
            ["ES_CONN_LOCAL_URL"] = "http://es:9200",
        });

        Assert.That(result.Connections.Single().GetBool("SSL_SKIP_VERIFY"), Is.False);
    }

    [Test]
    public void Parse_MissingRequiredField_RejectsWithVariableName() {
        ConnectionParseResult result = Parse(ConnectionSchemas.SqlServer, new() {
            ["MSSQL_CONN_PROD_NAME"] = "production",
            ["MSSQL_CONN_PROD_PORT"] = "1433",
        });

        RejectedConnection rejected = result.Rejected.Single();
        Assert.Multiple(() => {
            Assert.That(rejected.Name, Is.EqualTo("production"));
            Assert.That(rejected.Errors, Has.Some.Contains("MSSQL_CONN_PROD_HOST is required"));
            Assert.That(rejected.Errors, Has.Some.Contains("MSSQL_CONN_PROD_PASSWORD is required"));
            Assert.That(result.Connections, Is.Empty);
        });
    }

    [TestCase("abc")]
    [TestCase("70000")]
    [TestCase("0")]
    public void Parse_InvalidPort_RejectsWithReason(string port) {
        ConnectionParseResult result = Parse(ConnectionSchemas.Redis, new() {
            ["REDIS_CONN_LAB_HOST"] = "redis",
            ["REDIS_CONN_LAB_PORT"] = port,
        });

        Assert.That(result.Rejected.Single().Errors.Single(), Does.Contain("REDIS_CONN_LAB_PORT must be a port number"));
    }

    [TestCase("SSL_SKIP_VERIFY", "yes", "true or false")]
    [TestCase("URL", "es:9200", "absolute http or https URL")]
    public void Parse_InvalidValue_RejectsWithReason(string field, string value, string expected) {
        Dictionary<string, string> snapshot = new() {
            ["ES_CONN_LAB_URL"] = "http://es:9200",
            [$"ES_CONN_LAB_{field}"] = value,
        };

        ConnectionParseResult result = Parse(ConnectionSchemas.Elasticsearch, snapshot);

        Assert.That(result.Rejected.SelectMany(item => item.Errors), Has.Some.Contains(expected));
    }

    [Test]
    public void Parse_UnknownField_ReportsIgnoredSettingWithKnownFields() {
        ConnectionParseResult result = Parse(ConnectionSchemas.SqlServer, new() {
            ["MSSQL_CONN_PROD_HOTS"] = "db",
        });

        IgnoredSetting ignored = result.Ignored.Single();
        Assert.Multiple(() => {
            Assert.That(ignored.Key, Is.EqualTo("MSSQL_CONN_PROD_HOTS"));
            Assert.That(ignored.Reason, Does.Contain("Unknown field HOTS"));
            Assert.That(ignored.Reason, Does.Contain("HOST"));
        });
    }

    [Test]
    public void Parse_AliasWithUnderscore_ReportsAliasProblem() {
        ConnectionParseResult result = Parse(ConnectionSchemas.SqlServer, new() {
            ["MSSQL_CONN_MY_DB_HOST"] = "db",
        });

        Assert.That(result.Ignored.Single().Reason, Does.Contain("ALIAS cannot contain underscores: MY_DB. Use MYDB instead."));
    }

    [Test]
    public void Parse_LowerCaseAlias_ReportsAliasProblem() {
        ConnectionParseResult result = Parse(ConnectionSchemas.Redis, new() {
            ["REDIS_CONN_local_HOST"] = "redis",
        });

        Assert.That(result.Ignored.Single().Reason, Does.Contain("upper-case"));
    }

    [Test]
    public void Parse_DuplicateName_RejectsLaterConnection() {
        ConnectionParseResult result = Parse(ConnectionSchemas.Mailpit, new() {
            ["MAILPIT_CONN_A_NAME"] = "mail",
            ["MAILPIT_CONN_A_URL"] = "http://a:8025",
            ["MAILPIT_CONN_B_NAME"] = "mail",
            ["MAILPIT_CONN_B_URL"] = "http://b:8025",
        });

        Assert.Multiple(() => {
            Assert.That(result.Connections.Single().Alias, Is.EqualTo("A"));
            Assert.That(result.Rejected.Single().Errors.Single(), Does.Contain("already used by MAILPIT_CONN_A_NAME"));
        });
    }

    [Test]
    public void Parse_RejectedConnectionName_IsNotTakenOverByAnotherAlias() {
        ConnectionParseResult result = Parse(ConnectionSchemas.Mailpit, new() {
            ["MAILPIT_CONN_A_NAME"] = "mail",
            ["MAILPIT_CONN_A_URL"] = "broken",
            ["MAILPIT_CONN_B_NAME"] = "mail",
            ["MAILPIT_CONN_B_URL"] = "http://b:8025",
        });

        Assert.Multiple(() => {
            Assert.That(result.Connections, Is.Empty);
            Assert.That(result.Rejected.Select(item => item.Alias), Is.EqualTo(new[] { "A", "B" }));
        });
    }

    [Test]
    public void Parse_BlankTemplate_SkipsSilently() {
        ConnectionParseResult result = Parse(ConnectionSchemas.SqlServer, new() {
            ["MSSQL_CONN_PROD_NAME"] = "",
            ["MSSQL_CONN_PROD_HOST"] = " ",
        });

        Assert.Multiple(() => {
            Assert.That(result.Connections, Is.Empty);
            Assert.That(result.Rejected, Is.Empty);
            Assert.That(result.Ignored, Is.Empty);
        });
    }

    [Test]
    public void Parse_FieldAlias_UsesCanonicalField() {
        ConnectionParseResult result = Parse(ConnectionSchemas.RabbitMq, new() {
            ["RABBITMQ_CONN_LAB_HOST"] = "rabbit",
            ["RABBITMQ_CONN_LAB_MGMT_PORT"] = "15673",
            ["RABBITMQ_CONN_LAB_USER"] = "guest",
            ["RABBITMQ_CONN_LAB_PASSWORD"] = "guest",
        });

        Assert.That(result.Connections.Single().GetInt("MGMTPORT"), Is.EqualTo(15673));
    }

    [Test]
    public void Parse_ApiContractWithoutSpec_RejectsWithRequireAnyOfMessage() {
        ConnectionParseResult result = Parse(ConnectionSchemas.ApiContract, new() {
            ["API_CONTRACT_CONN_LAB_NAME"] = "lab",
        });

        Assert.That(
            result.Rejected.Single().Errors,
            Has.Some.Contains("API_CONTRACT_CONN_LAB_SPEC_URL or API_CONTRACT_CONN_LAB_SPEC_PATH is required")
        );
    }

    [Test]
    public void RegistryCore_InvalidReload_KeepsPreviousConfigAndReportsState() {
        MutableSnapshotProvider provider = new(new() {
            ["MAILPIT_CONN_LAB_URL"] = "http://mail:8025",
        });
        using ConnectionRegistryCore<string> core = new(
            ConnectionSchemas.Mailpit,
            provider,
            Substitute.For<ILogger>(),
            values => values.GetString("URL")
        );

        provider.Reload(new() {
            ["MAILPIT_CONN_LAB_URL"] = "not-a-url",
            ["MAILPIT_CONN_LAB_TYPO"] = "x",
        });

        ConnectionStatus status = core.Status.Connections.Single();
        Assert.Multiple(() => {
            Assert.That(core.Get("lab"), Is.EqualTo("http://mail:8025"));
            Assert.That(status.State, Is.EqualTo("rejected_using_previous"));
            Assert.That(status.Errors.Single(), Does.Contain("MAILPIT_CONN_LAB_URL"));
            Assert.That(core.Status.Ignored.Single().Key, Is.EqualTo("MAILPIT_CONN_LAB_TYPO"));
        });
    }

    [Test]
    public void RegistryCore_FirstConnectionRejected_StaysTheDefault() {
        MutableSnapshotProvider provider = new(new() {
            ["MAILPIT_CONN_PROD_URL"] = "http://prod:8025",
            ["MAILPIT_CONN_TEST_URL"] = "http://test:8025",
        });
        using ConnectionRegistryCore<string> core = new(
            ConnectionSchemas.Mailpit,
            provider,
            Substitute.For<ILogger>(),
            values => values.GetString("URL")
        );

        provider.Reload(new() {
            ["MAILPIT_CONN_PROD_URL"] = "not-a-url",
            ["MAILPIT_CONN_TEST_URL"] = "http://test:8025",
        });

        Assert.Multiple(() => {
            Assert.That(core.Get(""), Is.EqualTo("http://prod:8025"));
            Assert.That(core.All.Keys, Is.EqualTo(new[] { "prod", "test" }));
        });
    }

    [Test]
    public void RegistryCore_ConnectionRemovedFromSnapshot_DropsConnection() {
        MutableSnapshotProvider provider = new(new() {
            ["MAILPIT_CONN_LAB_URL"] = "http://mail:8025",
        });
        using ConnectionRegistryCore<string> core = new(
            ConnectionSchemas.Mailpit,
            provider,
            Substitute.For<ILogger>(),
            values => values.GetString("URL")
        );

        provider.Reload([]);

        Assert.Multiple(() => {
            Assert.That(core.All, Is.Empty);
            Assert.That(core.Status.Connections, Is.Empty);
        });
    }

    private static ConnectionParseResult Parse(ConnectionSchema schema, Dictionary<string, string> snapshot) =>
        ConnectionSettingsParser.Parse(schema, snapshot);

    private sealed class MutableSnapshotProvider : IDotEnvSnapshotProvider {
        public MutableSnapshotProvider(Dictionary<string, string> current) {
            Current = current;
        }

        public IReadOnlyDictionary<string, string> Current { get; private set; }

        public event EventHandler<DotEnvReloadedEventArgs>? Reloaded;

        public void Reload(Dictionary<string, string> next) {
            IReadOnlyDictionary<string, string> previous = Current;
            Current = next;
            Reloaded?.Invoke(this, new DotEnvReloadedEventArgs(previous, next, [], [], []));
        }
    }
}
