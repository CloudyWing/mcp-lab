using CloudyWing.McpLab.Oidc;
using CloudyWing.McpLab.Shared;
using CloudyWing.McpLab.Shared.DotEnv;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
string dotEnvPath = Environment.GetEnvironmentVariable("MCPLAB_DOTENV_PATH") ?? "/app/config/service.env";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddDotEnvSnapshot(dotEnvPath);
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<OidcDocumentClient>();
builder.Services.AddHttpClient(ConnectionRegistry.ClientKey)
    .ConfigureHttpClient(c => {
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
    .WithToolErrorFlag()
    .WithTools<OidcTools>();

WebApplication app = builder.Build();
app.UseMcpAccessToken();
app.MapMcpHealth("mcp-oidc");
app.MapConfigStatus<ConnectionRegistry>("mcp-oidc");
app.MapMcp("/mcp");
await app.RunAsync();
