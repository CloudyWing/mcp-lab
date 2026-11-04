using CloudyWing.McpLab.Oracle;
using CloudyWing.McpLab.Shared;
using CloudyWing.McpLab.Shared.Configuration;
using CloudyWing.McpLab.Shared.DotEnv;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
string dotEnvPath = Environment.GetEnvironmentVariable("MCPLAB_DOTENV_PATH") ?? "/app/config/service.env";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddDotEnvSnapshot(dotEnvPath);
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services
    .AddMcpServer(
        o => o.ServerInfo = new() {
            Name = "mcp-oracle",
            Version = "1.0.0"
        }
    )
    .WithHttpTransport()
    .WithTools<OracleTools>();

WebApplication app = builder.Build();
app.UseMcpAccessToken();
app.MapMcpHealth("mcp-oracle");
app.MapConfigStatus<ConnectionRegistry>(
    "mcp-oracle",
    services => ConfigStatusEndpoint.ToolData(ActivatorUtilities.CreateInstance<OracleTools>(services).ListRuntimeLimits())
);
app.MapMcp("/mcp");
await app.RunAsync();
