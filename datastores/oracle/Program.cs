using CloudyWing.McpLab.Oracle;
using CloudyWing.McpLab.Shared;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
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
app.MapMcpHealth("mcp-oracle");
app.MapMcp("/mcp");
await app.RunAsync();
