using CloudyWing.McpLab.SqlServer;
using CloudyWing.McpLab.Shared;
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
            Name = "mcp-sql-server",
            Version = "1.0.0"
        }
    )
    .WithHttpTransport()
    .WithToolErrorFlag()
    .WithTools<SqlServerTools>();

WebApplication app = builder.Build();
app.UseMcpAccessToken();
app.MapMcpHealth("mcp-sql-server");
app.MapConfigStatus<ConnectionRegistry>(
    "mcp-sql-server",
    services => ConfigStatusEndpoint.ToolData(ActivatorUtilities.CreateInstance<SqlServerTools>(services).ListRuntimeLimits())
);
app.MapMcp("/mcp");
await app.RunAsync();
