using CloudyWing.McpLab.Redis;
using CloudyWing.McpLab.Shared;
using CloudyWing.McpLab.Shared.DotEnv;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
string dotEnvPath = Environment.GetEnvironmentVariable("MCPLAB_DOTENV_PATH") ?? "/app/.env";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddDotEnvSnapshot(dotEnvPath);
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<RedisToolLimits>();
builder.Services
    .AddMcpServer(
        o => o.ServerInfo = new() {
            Name = "mcp-redis",
            Version = "1.0.0"
        }
    )
    .WithHttpTransport()
    .WithTools<RedisTools>();

WebApplication app = builder.Build();
app.MapMcpHealth("mcp-redis");
app.MapMcp("/mcp");
await app.RunAsync();
