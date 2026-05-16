using CloudyWing.McpLab.Oidc;
using CloudyWing.McpLab.Shared;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<OidcDocumentClient>();
builder.Services.AddHttpClient(ConnectionRegistry.ClientKey)
    .ConfigureHttpClient(c => {
        c.Timeout = TimeSpan.FromSeconds(10);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    });
builder.Services
    .AddMcpServer(
        o => o.ServerInfo = new() {
            Name = "mcp-oidc",
            Version = "1.0.0"
        }
    )
    .WithHttpTransport()
    .WithTools<OidcTools>();

WebApplication app = builder.Build();
app.MapMcpHealth("mcp-oidc");
app.MapMcp("/mcp");
await app.RunAsync();
