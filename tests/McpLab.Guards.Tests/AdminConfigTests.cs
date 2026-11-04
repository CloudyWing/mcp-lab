using CloudyWing.McpLab.Admin.Services;
using CloudyWing.McpLab.Shared.Configuration;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class AdminConfigTests {
    private string root = "";

    [SetUp]
    public void SetUp() {
        root = Path.Combine(Path.GetTempPath(), "mcplab-admin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown() {
        Directory.Delete(root, recursive: true);
    }

    [Test]
    public void EnvFile_SetAndRemove_KeepsCommentsAndOrder() {
        EnvFile file = EnvFile.Parse("# SQL\nMSSQL_CONN_A_HOST=old\n\n# limits\nMSSQL_MAX_ROWS=500\nMSSQL_CONN_A_HOST=dup\n");

        file.Set("MSSQL_CONN_A_HOST", "new");
        file.Remove("MSSQL_MAX_ROWS");
        file.Set("MSSQL_CONN_A_USER", "sa");

        Assert.That(file.ToString(), Is.EqualTo("# SQL\nMSSQL_CONN_A_HOST=new\n\n# limits\nMSSQL_CONN_A_USER=sa\n"));
    }

    [TestCase("plain")]
    [TestCase("with space inside")]
    [TestCase(" leading and trailing ")]
    [TestCase("\"quoted\"")]
    [TestCase("'single'")]
    [TestCase("back\\slash")]
    [TestCase("multi\nline")]
    [TestCase("p@ss#word=1")]
    public void EnvFile_Quote_RoundTripsThroughParser(string value) {
        EnvFile file = EnvFile.Parse("");
        file.Set("KEY", value);

        Assert.That(EnvFile.Parse(file.ToString()).Values["KEY"], Is.EqualTo(value));
    }

    [Test]
    public void EnvFile_InvalidSyntax_Throws() {
        Assert.Throws<FormatException>(() => EnvFile.Parse("KEY=\"unterminated\n"));
    }

    [Test]
    public void ConfigEditor_RenameAlias_MovesKeysAndKeepsSecret() {
        EnvFile file = EnvFile.Parse("MSSQL_CONN_OLD_HOST=db\nMSSQL_CONN_OLD_USER=sa\nMSSQL_CONN_OLD_PASSWORD=secret\n");
        ConnectionDraft draft = new() { OriginalAlias = "OLD", Alias = "NEW" };
        draft.Values["HOST"] = "db2";
        draft.Values["USER"] = "sa";

        ConfigEditor.Apply(ConnectionSchemas.SqlServer, file, draft);

        IReadOnlyDictionary<string, string> values = file.Values;
        Assert.Multiple(() => {
            Assert.That(values["MSSQL_CONN_NEW_HOST"], Is.EqualTo("db2"));
            Assert.That(values["MSSQL_CONN_NEW_PASSWORD"], Is.EqualTo("secret"));
            Assert.That(values.Keys.Any(key => key.StartsWith("MSSQL_CONN_OLD_", StringComparison.Ordinal)), Is.False);
        });
    }

    [Test]
    public void ConfigEditor_ReplaceAndClearSecret_UpdatesOnlyRequestedSecret() {
        EnvFile file = EnvFile.Parse("ES_CONN_A_URL=http://es:9200\nES_CONN_A_USER=elastic\nES_CONN_A_PASSWORD=old\n");
        ConnectionDraft draft = new() { OriginalAlias = "A", Alias = "A" };
        draft.Values["URL"] = "http://es:9200";
        draft.Values["USER"] = "";
        draft.ClearedSecrets.Add("PASSWORD");

        ConfigEditor.Apply(ConnectionSchemas.Elasticsearch, file, draft);

        Assert.That(file.Values.Keys, Is.EquivalentTo(new[] { "ES_CONN_A_URL" }));
    }

    [Test]
    public void ConfigEditor_ListConnections_HidesSecretValues() {
        EnvFile file = EnvFile.Parse("REDIS_CONN_LAB_HOST=redis\nREDIS_CONN_LAB_PASSWORD=topsecret\n");

        ConnectionEntry entry = ConfigEditor.ListConnections(ConnectionSchemas.Redis, file).Single();

        Assert.Multiple(() => {
            Assert.That(entry.SecretsSet, Does.Contain("PASSWORD"));
            Assert.That(entry.Values.Values, Has.None.EqualTo("topsecret"));
            Assert.That(entry.State, Is.EqualTo("loaded"));
            Assert.That(entry.Name, Is.EqualTo("lab"));
        });
    }

    [Test]
    public void ConfigEditor_Validate_ReportsMissingRequiredFieldsAndDuplicateAlias() {
        EnvFile file = EnvFile.Parse("MSSQL_CONN_A_HOST=db\nMSSQL_CONN_A_USER=sa\nMSSQL_CONN_A_PASSWORD=x\n");
        ConnectionDraft missing = new() { Alias = "B" };
        missing.Values["HOST"] = "db";
        ConnectionDraft duplicate = new() { Alias = "A" };
        ConnectionDraft badAlias = new() { Alias = "my_db" };

        Assert.Multiple(() => {
            Assert.That(ConfigEditor.Validate(ConnectionSchemas.SqlServer, file, missing), Has.Some.Contains("MSSQL_CONN_B_USER is required"));
            Assert.That(ConfigEditor.Validate(ConnectionSchemas.SqlServer, file, duplicate), Has.Some.Contains("已經存在"));
            Assert.That(ConfigEditor.Validate(ConnectionSchemas.SqlServer, file, badAlias), Has.Some.Contains("大寫英文字母與數字"));
        });
    }

    [Test]
    public void ConfigEditor_CopyDraft_CopiesSecretsAndClearsName() {
        EnvFile file = EnvFile.Parse("MSSQL_CONN_STAGE_NAME=staging\nMSSQL_CONN_STAGE_HOST=db\nMSSQL_CONN_STAGE_USER=sa\nMSSQL_CONN_STAGE_PASSWORD=pw\n");

        ConnectionDraft draft = ConfigEditor.CopyDraft(ConnectionSchemas.SqlServer, file, "STAGE", "STAGE2");
        ConfigEditor.Apply(ConnectionSchemas.SqlServer, file, draft);

        Assert.Multiple(() => {
            Assert.That(file.Values["MSSQL_CONN_STAGE2_PASSWORD"], Is.EqualTo("pw"));
            Assert.That(file.Values.ContainsKey("MSSQL_CONN_STAGE2_NAME"), Is.False);
            Assert.That(ConfigEditor.ListConnections(ConnectionSchemas.SqlServer, file).Select(entry => entry.State), Is.All.EqualTo("loaded"));
        });
    }

    [Test]
    public void ConfigEditor_Delete_RemovesOnlyThatAlias() {
        EnvFile file = EnvFile.Parse("MAILPIT_CONN_A_URL=http://a\nMAILPIT_CONN_AB_URL=http://ab\n");

        ConfigEditor.Delete(ConnectionSchemas.Mailpit, file, "A");

        Assert.That(file.Values.Keys, Is.EquivalentTo(new[] { "MAILPIT_CONN_AB_URL" }));
    }

    [Test]
    public void ConfigEditor_SetSetting_RemovesFallbackNames() {
        EnvFile file = EnvFile.Parse("QUERY_TIMEOUT=90\n");

        ConfigEditor.SetSetting(file, "MSSQL_QUERY_TIMEOUT", ["QUERY_TIMEOUT"], "30");

        Assert.That(file.ToString(), Is.EqualTo("MSSQL_QUERY_TIMEOUT=30\n"));
    }

    [Test]
    public async Task ConfigStore_Write_IsPrivateAndBacksUpPrevious() {
        ConfigStore store = new(root);

        await store.WriteAsync("mcp-redis", "REDIS_CONN_A_HOST=one\n");
        await store.WriteAsync("mcp-redis", "REDIS_CONN_A_HOST=two\n");

        ConfigRevision revision = store.ListRevisions("mcp-redis").Single();
        Assert.Multiple(() => {
            Assert.That(store.Read("mcp-redis"), Is.EqualTo("REDIS_CONN_A_HOST=two\n"));
            Assert.That(store.ReadRevision("mcp-redis", revision.Id), Is.EqualTo("REDIS_CONN_A_HOST=one\n"));
            Assert.That(Directory.GetFiles(Path.Combine(root, "mcp-redis")), Has.Length.EqualTo(1));
        });

        if (!OperatingSystem.IsWindows()) {
            Assert.That(File.GetUnixFileMode(store.PathOf("mcp-redis")), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
    }

    [Test]
    public void ConfigStore_InvalidSyntaxOrUnknownService_IsRejected() {
        ConfigStore store = new(root);

        Assert.Multiple(() => {
            Assert.ThrowsAsync<FormatException>(() => store.WriteAsync("mcp-redis", "BROKEN"));
            Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync("../etc", "A=1\n"));
            Assert.Throws<ArgumentException>(() => store.ReadRevision("mcp-redis", "../x.env"));
            Assert.That(File.Exists(store.PathOf("mcp-redis")), Is.False);
        });
    }

    [Test]
    public void ConfigApplier_FindRegressions_ReportsOnlyNewProblems() {
        ServiceStatus before = new() {
            Connections = [
                new() { Alias = "A", Name = "a", State = "loaded" },
                new() { Alias = "B", Name = "b", State = "rejected", Errors = ["old"] },
            ],
        };
        ServiceStatus after = new() {
            Connections = [
                new() { Alias = "A", Name = "a", State = "rejected_using_previous", Errors = ["port"] },
                new() { Alias = "B", Name = "b", State = "rejected", Errors = ["old"] },
                new() { Alias = "C", Name = "c", State = "rejected", Errors = ["new"] },
            ],
        };

        IReadOnlyList<string> problems = ConfigApplier.FindRegressions(before, after);

        Assert.That(problems, Is.EqualTo(new[] { "A（a）：port", "C（c）：new" }));
    }

    [Test]
    public void ConfigApplier_FindRegressions_ReportsSyntaxError() {
        ServiceStatus after = new() { ConfigFile = new() { Exists = true, LastError = "line 3" } };

        Assert.That(ConfigApplier.FindRegressions(new ServiceStatus(), after), Is.EqualTo(new[] { "line 3" }));
    }

    [Test]
    public async Task EnvImporter_Preview_SplitsKeysAndSkipsComposeSettings() {
        ConfigStore store = new(root);
        await store.WriteAsync("mcp-redis", "# existing\nREDIS_MAX_ROWS=50\n");
        const string env = "MCP_SQL_SERVER_PORT=9800\nMSSQL_CONN_A_HOST=db\nMSSQL_CONN_A_USER=sa\nMSSQL_CONN_A_PASSWORD=pw\n"
            + "REDIS_CONN_L_HOST=redis\nMCPLAB_MCP_TOKEN=tok\nDOCUMENT_HOST_MOUNT_ROOT=/docs\nDOCUMENT_MAX_IMAGES=5\nMAX_ROWS=10\nUNKNOWN_KEY=1\n"
            + "MSSQL_CONN_MY_DB_HOST=x\n";

        ImportPreview preview = EnvImporter.Preview(env, store);
        Dictionary<string, ImportItem> items = preview.Items.ToDictionary(item => item.Key);

        Assert.Multiple(() => {
            Assert.That(items["MCP_SQL_SERVER_PORT"].Note, Does.Contain("Compose"));
            Assert.That(items["DOCUMENT_HOST_MOUNT_ROOT"].Note, Does.Contain("Compose"));
            Assert.That(items["UNKNOWN_KEY"].Services, Is.Empty);
            Assert.That(items["DOCUMENT_MAX_IMAGES"].Services, Is.EqualTo(new[] { "mcp-reader" }));
            Assert.That(items["MAX_ROWS"].Services, Is.EquivalentTo(new[] { "mcp-sql-server", "mcp-oracle", "mcp-elasticsearch", "mcp-redis" }));
            Assert.That(items["MCPLAB_MCP_TOKEN"].Services, Has.Count.EqualTo(ServiceCatalog.All.Count));
            Assert.That(items["MSSQL_CONN_A_PASSWORD"].IsSecret, Is.True);
            Assert.That(preview.MergedFiles["mcp-redis"], Does.StartWith("# existing\nREDIS_MAX_ROWS=50\n"));
            Assert.That(preview.Checks.Single(check => check.Service == "mcp-sql-server").Ignored.Single().Key, Is.EqualTo("MSSQL_CONN_MY_DB_HOST"));
            Assert.That(items["MSSQL_CONN_MY_DB_HOST"].Services, Is.Empty);
            Assert.That(preview.MergedFiles["mcp-sql-server"], Does.Not.Contain("MSSQL_CONN_MY_DB_HOST"));
            Assert.That(store.Read("mcp-sql-server"), Is.Empty);
        });
    }

    [TestCase(null, "anything", false)]
    [TestCase("short", "short", false)]
    [TestCase("correct horse", "correct horse", true)]
    [TestCase("correct horse", "wrong horse", false)]
    public void AdminPassword_Verify_RequiresConfiguredMatchingPassword(string? configured, string candidate, bool expected) {
        Assert.That(new AdminPassword(configured).Verify(candidate), Is.EqualTo(expected));
    }

    [TestCase("""{"ok":true,"kind":"ok","message":"OK"}""", true, "連線成功。")]
    [TestCase("""{"ok":false,"kind":"error","message":"Login failed"}""", false, "Login failed")]
    [TestCase("not json", false, "not json")]
    public void ConnectionTester_Interpret_ReadsToolEnvelope(string text, bool success, string message) {
        ConnectionTestResult result = ConnectionTester.Interpret(text);

        Assert.That((result.Success, result.Message), Is.EqualTo((success, message)));
    }

    [Test]
    public void ConfigDiff_Compare_HidesSecretValues() {
        IReadOnlyList<ConfigChange> changes = ConfigDiff.Compare(
            "REDIS_CONN_A_HOST=old\nREDIS_CONN_A_PASSWORD=one\nREDIS_MAX_ROWS=10\n",
            "REDIS_CONN_A_HOST=new\nREDIS_CONN_A_PASSWORD=two\nREDIS_CONN_B_HOST=b\n"
        );

        Assert.That(changes, Is.EqualTo(new[] {
            new ConfigChange("REDIS_CONN_A_HOST", "changed", "old", "new", false),
            new ConfigChange("REDIS_CONN_A_PASSWORD", "changed", null, null, true),
            new ConfigChange("REDIS_CONN_B_HOST", "added", null, "b", false),
            new ConfigChange("REDIS_MAX_ROWS", "removed", "10", null, false),
        }));
    }

    [Test]
    public async Task AuditLog_ReadLatest_ReturnsNewestFirstAndFilters() {
        AuditLog log = new(new ConfigStore(root));
        await log.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "mcp-redis", "新增連線 A", ApplyOutcome.Applied, "ok"));
        await log.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "mcp-sql-server", "修改服務設定", ApplyOutcome.RolledBack, "rolled back"));
        await File.AppendAllTextAsync(Path.Combine(root, ".admin", "audit.jsonl"), "not json\n");

        Assert.Multiple(() => {
            Assert.That(log.ReadLatest(10).Select(entry => entry.Action), Is.EqualTo(new[] { "修改服務設定", "新增連線 A" }));
            Assert.That(log.ReadLatest(10, "mcp-redis").Single().Outcome, Is.EqualTo(ApplyOutcome.Applied));
        });
    }

    [Test]
    public void ConnectionSettingsParser_EnvironmentChoice_NormalizesCaseAndRejectsUnknownValue() {
        ConnectionParseResult ok = ConnectionSettingsParser.Parse(ConnectionSchemas.Mailpit, new Dictionary<string, string> {
            ["MAILPIT_CONN_A_URL"] = "http://a",
            ["MAILPIT_CONN_A_ENVIRONMENT"] = "PROD",
        });
        ConnectionParseResult bad = ConnectionSettingsParser.Parse(ConnectionSchemas.Mailpit, new Dictionary<string, string> {
            ["MAILPIT_CONN_A_URL"] = "http://a",
            ["MAILPIT_CONN_A_ENVIRONMENT"] = "production",
        });

        Assert.Multiple(() => {
            Assert.That(ok.Connections.Single().GetString("ENVIRONMENT"), Is.EqualTo("prod"));
            Assert.That(bad.Rejected.Single().Errors.Single(), Does.Contain("must be one of: local, dev, test, staging, prod"));
        });
    }
}
