using CloudyWing.McpLab.SqlServer;
using CloudyWing.McpLab.Shared;
using CloudyWing.McpLab.Shared.DotEnv;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
string dotEnvPath = Environment.GetEnvironmentVariable("MCPLAB_DOTENV_PATH") ?? "/app/.env";
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
    .WithTools<SqlServerTools>();

WebApplication app = builder.Build();
app.UseMcpAccessToken();
app.MapMcpHealth("mcp-sql-server");
app.MapMcp("/mcp");
await app.RunAsync();
