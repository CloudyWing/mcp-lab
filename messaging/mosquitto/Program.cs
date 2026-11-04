using CloudyWing.McpLab.Mosquitto;
using CloudyWing.McpLab.Shared;
using CloudyWing.McpLab.Shared.Configuration;
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
            Name = "mcp-mosquitto",
            Version = "1.0.0"
        }
    )
    .WithHttpTransport()
    .WithTools<MqttTools>();

WebApplication app = builder.Build();
app.UseMcpAccessToken();
app.MapMcpHealth("mcp-mosquitto");
app.MapConfigStatus<ConnectionRegistry>("mcp-mosquitto");
app.MapMcp("/mcp");
await app.RunAsync();
