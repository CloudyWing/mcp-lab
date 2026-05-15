using CloudyWing.McpLab.Mailpit;
using CloudyWing.McpLab.Shared;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddHttpClient(ConnectionRegistry.ClientKey)
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services
    .AddMcpServer(
        o => o.ServerInfo = new() {
            Name = "mcp-mailpit",
            Version = "1.0.0"
        }
    )
    .WithHttpTransport()
    .WithTools<MailpitTools>();

WebApplication app = builder.Build();
app.MapMcpHealth("mcp-mailpit");
app.MapMcp("/mcp");
await app.RunAsync();
