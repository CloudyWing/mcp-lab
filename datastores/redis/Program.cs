using CloudyWing.McpLab.Redis;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddSingleton<ConnectionRegistry>();
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
app.MapMcp("/mcp");
await app.RunAsync();
