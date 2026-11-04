using AngleSharp.Dom;
using Bunit;
using CloudyWing.McpLab.Admin.Components.Pages;
using Microsoft.AspNetCore.Components.Web;
using NUnit.Framework;

namespace CloudyWing.McpLab.Admin.Tests;

internal sealed class ServicePageTests : AdminTestContext {
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private const string RedisConfig = """
        # Redis
        REDIS_CONN_LAB_HOST=redis
        REDIS_CONN_LAB_PASSWORD=secret-value
        REDIS_CONN_LAB_ENVIRONMENT=prod
        REDIS_CONN_TEST_HOST=redis-test
        """;

    [Test]
    public void Render_ListsConnectionsWithStateAndEnvironment() {
        WriteConfig("mcp-redis", RedisConfig);

        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ServicePage>(("Name", "mcp-redis"));

        cut.WaitForAssertion(() => {
            IReadOnlyList<IElement> rows = ConnectionRows(cut);
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].TextContent, Does.Contain("LAB").And.Contain("redis").And.Not.Contain("secret-value"));
            Assert.That(rows[0].QuerySelector(".env-chip")?.TextContent.Trim(), Is.EqualTo("prod"));
            Assert.That(rows[0].TextContent, Does.Contain("已載入"));
        }, Wait);
    }

    [Test]
    public async Task SaveConnection_MissingRequiredField_ShowsErrorsAndKeepsFile() {
        WriteConfig("mcp-sql-server", "");
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ServicePage>(("Name", "mcp-sql-server"));
        cut.WaitForAssertion(() => Assert.That(Button(cut, "新增連線").HasAttribute("disabled"), Is.False), Wait);

        Button(cut, "新增連線").Click();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".mud-dialog"), Is.Not.Empty), Wait);
        Input(cut, "Alias").Input("NEW");
        Input(cut, "HOST *").Input("db");
        await Button(cut, "儲存").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => {
            string dialog = cut.Find(".mud-dialog").TextContent;
            Assert.That(dialog, Does.Contain("MSSQL_CONN_NEW_USER is required"));
            Assert.That(dialog, Does.Contain("MSSQL_CONN_NEW_PASSWORD is required"));
        }, Wait);
        Assert.That(Store.Read("mcp-sql-server"), Is.Empty);
    }

    [Test]
    public async Task SaveConnection_ValidDraft_WritesFileAndReportsApplied() {
        WriteConfig("mcp-sql-server", "# keep me\n");
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ServicePage>(("Name", "mcp-sql-server"));
        cut.WaitForAssertion(() => Assert.That(Button(cut, "新增連線").HasAttribute("disabled"), Is.False), Wait);

        Button(cut, "新增連線").Click();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".mud-dialog"), Is.Not.Empty), Wait);
        Input(cut, "Alias").Input("prod");
        Input(cut, "HOST *").Input("db");
        Input(cut, "USER *").Input("sa");
        Input(cut, "PASSWORD *").Input("p@ss word");
        await Button(cut, "儲存").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("已儲存並生效")), Wait);
        string file = Store.Read("mcp-sql-server");
        Assert.Multiple(() => {
            Assert.That(file, Does.StartWith("# keep me\n"));
            Assert.That(file, Does.Contain("MSSQL_CONN_PROD_HOST=db"));
            Assert.That(file, Does.Contain("MSSQL_CONN_PROD_PASSWORD=p@ss word"));
            Assert.That(ConnectionRows(cut).Single().TextContent, Does.Contain("PROD"));
        });
    }

    [Test]
    public async Task EditConnection_SecretIsNotShownAndKeptWhenLeftEmpty() {
        WriteConfig("mcp-redis", RedisConfig);
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ServicePage>(("Name", "mcp-redis"));
        cut.WaitForAssertion(() => Assert.That(ConnectionRows(cut), Has.Count.EqualTo(2)), Wait);

        cut.Find("button[aria-label='編輯 LAB']").Click();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".mud-dialog"), Is.Not.Empty), Wait);
        IElement password = Input(cut, "PASSWORD");
        Assert.Multiple(() => {
            Assert.That(password.GetAttribute("placeholder"), Is.EqualTo("已設定，留空則保持不變"));
            Assert.That(password.GetAttribute("value") ?? "", Is.Empty);
            Assert.That(cut.Find(".mud-dialog").InnerHtml, Does.Not.Contain("secret-value"));
        });

        Input(cut, "PORT").Input("6380");
        await Button(cut, "儲存").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.That(Store.Read("mcp-redis"), Does.Contain("REDIS_CONN_LAB_PORT=6380")), Wait);
        Assert.That(Store.Read("mcp-redis"), Does.Contain("REDIS_CONN_LAB_PASSWORD=secret-value"));
    }

    [Test]
    public async Task CloseResultAlert_AfterSave_DoesNotThrow() {
        // Regression: the alert content read the cleared field while MudAlert re-rendered, crashing the circuit.
        WriteConfig("mcp-redis", RedisConfig);
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ServicePage>(("Name", "mcp-redis"));
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".settings-grid input"), Is.Not.Empty), Wait);

        Input(cut, "REDIS_MAX_ROWS").Input("50");
        await Button(cut, "儲存服務設定").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".page-stack > .mud-alert"), Is.Not.Empty), Wait);

        Assert.DoesNotThrow(() => cut.Find(".page-stack > .mud-alert .mud-alert-close-button").Click());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".page-stack > .mud-alert"), Is.Empty), Wait);
    }

    [Test]
    public async Task SaveSettings_OutOfRange_ShowsRangeErrorAndKeepsFile() {
        WriteConfig("mcp-redis", RedisConfig);
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ServicePage>(("Name", "mcp-redis"));
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".settings-grid input"), Is.Not.Empty), Wait);

        Input(cut, "REDIS_MAX_ROWS").Input("99999");
        await Button(cut, "儲存服務設定").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.That(cut.Find(".settings-grid").TextContent, Does.Contain("允許範圍 1 到 1000")), Wait);
        Assert.That(Store.Read("mcp-redis"), Does.Not.Contain("REDIS_MAX_ROWS"));
    }

    [Test]
    public async Task DeleteConnection_Confirmed_RemovesOnlyThatAlias() {
        WriteConfig("mcp-redis", RedisConfig);
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ServicePage>(("Name", "mcp-redis"));
        cut.WaitForAssertion(() => Assert.That(ConnectionRows(cut), Has.Count.EqualTo(2)), Wait);

        cut.Find("button[aria-label='刪除 TEST']").Click();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".mud-dialog"), Is.Not.Empty), Wait);
        await Button(cut, "確認刪除").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.That(ConnectionRows(cut).Where(row => row.TextContent.Contains("TEST")), Is.Empty), Wait);
        Assert.Multiple(() => {
            Assert.That(Store.Read("mcp-redis"), Does.Not.Contain("REDIS_CONN_TEST_"));
            Assert.That(Store.Read("mcp-redis"), Does.Contain("REDIS_CONN_LAB_HOST=redis"));
        });
    }

    [Test]
    public void Render_ServiceUnreachable_StillAllowsEditingAndWarns() {
        WriteConfig("mcp-redis", RedisConfig);
        Services.Reachable = false;

        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ServicePage>(("Name", "mcp-redis"));

        cut.WaitForAssertion(() => {
            Assert.That(cut.Markup, Does.Contain("無法連線到服務"));
            Assert.That(Button(cut, "新增連線").HasAttribute("disabled"), Is.False);
        }, Wait);
    }

    [Test]
    public async Task RestoreRevision_RevertsToBackup() {
        WriteConfig("mcp-redis", RedisConfig);
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ServicePage>(("Name", "mcp-redis"));
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".settings-grid input"), Is.Not.Empty), Wait);
        Input(cut, "REDIS_MAX_ROWS").Input("50");
        await Button(cut, "儲存服務設定").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.That(Store.Read("mcp-redis"), Does.Contain("REDIS_MAX_ROWS=50")), Wait);

        cut.WaitForAssertion(() => Assert.That(Button(cut, "還原此版本").HasAttribute("disabled"), Is.False), Wait);
        Button(cut, "還原此版本").Click();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".mud-dialog"), Is.Not.Empty), Wait);
        await Button(cut, "確認還原").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.That(Store.Read("mcp-redis"), Does.Not.Contain("REDIS_MAX_ROWS")), Wait);
    }

    private static IReadOnlyList<IElement> ConnectionRows(IRenderedComponent<ContainerFragment> cut) =>
        cut.FindAll(".content-card")
            .First(card => card.TextContent.Contains("新增連線"))
            .QuerySelectorAll("tbody tr")
            .Where(row => !row.ClassList.Contains("test-row"))
            .ToList();

    internal static IElement Button(IRenderedComponent<ContainerFragment> cut, string text) =>
        cut.FindAll("button").First(button => button.TextContent.Trim() == text);

    internal static IElement Input(IRenderedComponent<ContainerFragment> cut, string label) =>
        cut.FindAll(".mud-input-control")
            .First(control => control.QuerySelector("label")?.TextContent.Trim() == label)
            .QuerySelector("input, textarea")!;
}
