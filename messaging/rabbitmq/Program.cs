using CloudyWing.McpLab.RabbitMq;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddHttpClient(ConnectionRegistry.ClientKey)
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services
    .AddMcpServer(
        o => o.ServerInfo = new() {
            Name = "mcp-rabbitmq",
            Version = "1.0.0"
        }
    )
    .WithHttpTransport()
    .WithTools<RabbitMqTools>();

WebApplication app = builder.Build();
app.MapMcp("/mcp");
await app.RunAsync();
