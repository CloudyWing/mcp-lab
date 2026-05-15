using CloudyWing.McpLab.Docker;
using CloudyWing.McpLab.Shared;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddSingleton<DockerEngineClient>();
builder.Services
    .AddMcpServer(
        o => o.ServerInfo = new() {
            Name = "mcp-docker",
            Version = "1.0.0"
        }
    )
    .WithHttpTransport()
    .WithTools<DockerTools>();

WebApplication app = builder.Build();
app.MapMcpHealth("mcp-docker");
app.MapMcp("/mcp");
await app.RunAsync();
