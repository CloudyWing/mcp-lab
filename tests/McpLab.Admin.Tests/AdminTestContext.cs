using System.Net;
using System.Text.Json;
using Bunit;
using CloudyWing.McpLab.Admin.Services;
using CloudyWing.McpLab.Shared.Configuration;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NUnit.Framework;

namespace CloudyWing.McpLab.Admin.Tests;

/// <summary>
/// Renders admin components against a temporary config directory and fake MCP services whose
/// <c>/config/status</c> reflects the files on disk, so saves go through the real verification path.
/// </summary>
internal abstract class AdminTestContext {
    protected BunitContext Context { get; private set; } = null!;

    protected ConfigStore Store { get; private set; } = null!;

    protected FakeServiceHandler Services { get; private set; } = null!;

    private string root = "";

    [SetUp]
    public void SetUpContext() {
        root = Path.Combine(Path.GetTempPath(), "mcplab-admin-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Store = new ConfigStore(root);
        Services = new FakeServiceHandler(Store);
        ServiceEndpoints endpoints = new("http://{0}:8080");
        ServiceStatusClient statusClient = new(new HttpClient(Services), Store, endpoints);

        Context = new BunitContext();
        Context.JSInterop.Mode = JSRuntimeMode.Loose;
        Context.Services.AddMudServices();
        Context.Services.AddSingleton(Store);
        Context.Services.AddSingleton(endpoints);
        Context.Services.AddSingleton(statusClient);
        Context.Services.AddSingleton(new AuditLog(Store));
        Context.Services.AddSingleton(new ConnectionTester(Store, endpoints));
        Context.Services.AddSingleton(provider => new ConfigApplier(
            Store,
            statusClient,
            provider.GetRequiredService<AuditLog>(),
            TimeSpan.FromSeconds(3)
        ));
        Context.Services.AddSingleton(new AdminPassword("test-password"));
    }

    [TearDown]
    public async Task TearDownContext() {
        await Context.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// Writes a service config file directly, bypassing the admin.
    /// </summary>
    protected void WriteConfig(string service, string content) {
        string directory = Path.Combine(root, service);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ConfigStore.FileName), content);
    }

    /// <summary>
    /// Renders a component inside the MudBlazor providers that dialogs and popovers need.
    /// </summary>
    protected IRenderedComponent<ContainerFragment> RenderWithProviders<TComponent>(params (string Name, object? Value)[] parameters)
        where TComponent : IComponent {
        return Context.Render<ContainerFragment>(builder => builder.Add(fragment => fragment.ChildContent, (RenderFragment)(content => {
            content.OpenComponent<MudPopoverProvider>(0);
            content.CloseComponent();
            content.OpenComponent<MudDialogProvider>(1);
            content.CloseComponent();
            content.OpenComponent<MudSnackbarProvider>(2);
            content.CloseComponent();
            content.OpenComponent<TComponent>(3);

            foreach ((string name, object? value) in parameters) {
                content.AddAttribute(4, name, value);
            }

            content.CloseComponent();
        })));
    }
}

/// <summary>
/// Renders its child content; used to host providers next to the component under test.
/// </summary>
public sealed class ContainerFragment : ComponentBase {
    /// <summary>Gets or sets the content.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);
}

/// <summary>
/// Answers <c>/config/status</c> for every service from the config files, as a running service would after reloading.
/// </summary>
internal sealed class FakeServiceHandler : HttpMessageHandler {
    private readonly ConfigStore store;

    public FakeServiceHandler(ConfigStore store) {
        this.store = store;
    }

    /// <summary>Gets or sets whether the services answer at all.</summary>
    public bool Reachable { get; set; } = true;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        if (!Reachable) {
            throw new HttpRequestException("Connection refused");
        }

        string service = request.RequestUri!.Host;
        ManagedService? managed = ServiceCatalog.Find(service);

        if (managed is null || request.RequestUri.AbsolutePath != "/config/status") {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        IReadOnlyDictionary<string, string> values = EnvFile.Parse(store.Read(service)).Values;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        object? connections = null;
        object? ignored = null;

        if (managed.Schema is ConnectionSchema schema) {
            ConnectionParseResult parsed = ConnectionSettingsParser.Parse(schema, values);
            connections = parsed.Connections
                .Select(item => new { alias = item.Alias, name = item.Name, state = "loaded", errors = Array.Empty<string>() })
                .Concat(parsed.Rejected.Select(item => new { alias = item.Alias, name = item.Name, state = "rejected", errors = item.Errors.ToArray() }))
                .ToList();
            ignored = parsed.Ignored.Select(item => new { key = item.Key, reason = item.Reason }).ToList();
        }

        object body = new {
            service,
            config_file = new { exists = true, last_loaded_at = now, last_error = (string?)null },
            connections,
            ignored_settings = ignored,
            connections_loaded_at = managed.Schema is null ? (DateTimeOffset?)null : now,
            settings = managed.Settings.Select(setting => new {
                name = setting.Name,
                description = setting.Description,
                unit = setting.Unit,
                default_value = setting.DefaultValue,
                min = setting.Min,
                max = setting.Max,
                fallbacks = setting.Fallbacks ?? [],
                effective = setting.Read(values),
            }),
        };

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
