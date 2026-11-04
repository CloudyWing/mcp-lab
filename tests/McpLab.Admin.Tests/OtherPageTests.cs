using Bunit;
using CloudyWing.McpLab.Admin.Components.Pages;
using CloudyWing.McpLab.Admin.Components.Shared;
using CloudyWing.McpLab.Admin.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudyWing.McpLab.Admin.Tests;

internal sealed class OtherPageTests : AdminTestContext {
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Test]
    public void ImportPage_TypingEnablesPreviewWithoutLeavingTheField() {
        // Regression: without Immediate binding the button stayed disabled until the textarea lost focus.
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ImportPage>();
        Assert.That(ServicePageTests.Button(cut, "預覽").HasAttribute("disabled"), Is.True);

        cut.Find("textarea").Input("REDIS_CONN_A_HOST=redis");

        cut.WaitForAssertion(() => Assert.That(ServicePageTests.Button(cut, "預覽").HasAttribute("disabled"), Is.False), Wait);
    }

    [Test]
    public void ImportPage_Preview_ListsProblemsAndHidesSecretValues() {
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ImportPage>();
        cut.Find("textarea").Input("MCP_SQL_SERVER_PORT=9800\nREDIS_CONN_A_HOST=redis\nREDIS_CONN_A_PASSWORD=hunter2\nREDIS_CONN_A_HOTS=typo\n");

        ServicePageTests.Button(cut, "預覽").Click();

        cut.WaitForAssertion(() => {
            // The pasted text stays in the textarea, so only the preview card is checked for the secret.
            string markup = cut.FindAll(".content-card").Last().OuterHtml;
            Assert.That(markup, Does.Contain("Compose 層設定"));
            Assert.That(markup, Does.Contain("Unknown field HOTS"));
            Assert.That(markup, Does.Contain("REDIS_CONN_A_PASSWORD").And.Contain("值已隱藏"));
            Assert.That(markup, Does.Not.Contain("hunter2"));
        }, Wait);
        Assert.That(Store.Read("mcp-redis"), Is.Empty, "preview must not write files");
    }

    [Test]
    public async Task ImportPage_Apply_WritesServiceFilesWithoutIgnoredKeys() {
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<ImportPage>();
        cut.Find("textarea").Input("REDIS_CONN_A_HOST=redis\nREDIS_CONN_A_HOTS=typo\nDOCUMENT_MAX_IMAGES=5\n");
        ServicePageTests.Button(cut, "預覽").Click();
        cut.WaitForAssertion(() => Assert.That(ServicePageTests.Button(cut, "套用").HasAttribute("disabled"), Is.False), Wait);

        await ServicePageTests.Button(cut, "套用").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("匯入完成")), Wait);
        Assert.Multiple(() => {
            Assert.That(Store.Read("mcp-redis"), Does.Contain("REDIS_CONN_A_HOST=redis"));
            Assert.That(Store.Read("mcp-redis"), Does.Not.Contain("HOTS"));
            Assert.That(Store.Read("mcp-reader"), Does.Contain("DOCUMENT_MAX_IMAGES=5"));
        });
    }

    [Test]
    public async Task AccessPage_GenerateToken_AppliesSameTokenToEveryService() {
        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<AccessPage>();

        // Wait for the whole handler: it saves and verifies all eleven services one after another.
        await ServicePageTests.Button(cut, "產生新權杖並套用到所有服務").ClickAsync(new MouseEventArgs());

        string token = cut.Find(".token-value").TextContent.Trim();
        Assert.Multiple(() => {
            Assert.That(token, Has.Length.GreaterThanOrEqualTo(40));
            foreach (ManagedService service in ServiceCatalog.All) {
                Assert.That(Store.Read(service.Name), Does.Contain($"MCPLAB_MCP_TOKEN={token}"), service.Name);
            }

            Assert.That(cut.Markup, Does.Contain("已啟用：所有服務使用同一個權杖"));
        });
    }

    [Test]
    public void HistoryPage_ShowsChangesMadeThroughTheAdmin() {
        AuditLog audit = Context.Services.GetRequiredService<AuditLog>();
        audit.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "mcp-redis", "新增連線 LAB", ApplyOutcome.Applied, "ok")).GetAwaiter().GetResult();
        audit.AppendAsync(new AuditEntry(DateTimeOffset.UtcNow, "mcp-sql-server", "修改服務設定", ApplyOutcome.RolledBack, "rolled back")).GetAwaiter().GetResult();

        IRenderedComponent<ContainerFragment> cut = RenderWithProviders<HistoryPage>();

        Assert.Multiple(() => {
            Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2));
            Assert.That(cut.FindAll("tbody tr")[0].TextContent, Does.Contain("修改服務設定").And.Contain("已自動還原"));
        });
    }

    [TestCase("loaded", "已載入")]
    [TestCase("rejected_using_previous", "設定有誤，沿用前一版")]
    [TestCase("unreachable", "無法連線")]
    public void StateChip_ShowsLabelForState(string state, string label) {
        IRenderedComponent<StateChip> cut = Context.Render<StateChip>(parameters => parameters.Add(chip => chip.State, state));

        Assert.That(cut.Find(".mud-chip").TextContent.Trim(), Is.EqualTo(label));
    }

    [Test]
    public void Login_PasswordNotConfigured_ExplainsHowToSetIt() {
        Context.Services.AddSingleton(new AdminPassword(null));

        IRenderedComponent<Login> cut = Context.Render<Login>();

        Assert.Multiple(() => {
            Assert.That(cut.Markup, Does.Contain("MCPLAB_ADMIN_PASSWORD"));
            Assert.That(cut.FindAll("form"), Is.Empty);
        });
    }
}
