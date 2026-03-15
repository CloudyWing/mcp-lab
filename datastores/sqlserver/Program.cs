using CloudyWing.McpLab.SqlServer;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
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
app.MapMcp("/mcp");
await app.RunAsync();
