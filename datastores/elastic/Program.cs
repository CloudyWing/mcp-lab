using CloudyWing.McpLab.Elastic;
using CloudyWing.McpLab.Shared;
using CloudyWing.McpLab.Shared.DotEnv;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
string dotEnvPath = Environment.GetEnvironmentVariable("MCPLAB_DOTENV_PATH") ?? "/app/.env";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddDotEnvSnapshot(dotEnvPath);
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddHttpClient(ConnectionRegistry.PlainClientKey)
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient(ConnectionRegistry.SslSkipClientKey)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    })
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services
    .AddMcpServer(
        o => o.ServerInfo = new() {
            Name = "mcp-elasticsearch",
            Version = "1.0.0"
        }
    )
    .WithHttpTransport()
    .WithTools<ElasticTools>();

WebApplication app = builder.Build();
app.MapMcpHealth("mcp-elasticsearch");
app.MapMcp("/mcp");
await app.RunAsync();
