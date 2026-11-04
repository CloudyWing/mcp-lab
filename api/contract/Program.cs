using CloudyWing.McpLab.ApiContract;
using CloudyWing.McpLab.Shared;
using CloudyWing.McpLab.Shared.Configuration;
using CloudyWing.McpLab.Shared.DotEnv;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
string dotEnvPath = Environment.GetEnvironmentVariable("MCPLAB_DOTENV_PATH") ?? "/app/config/service.env";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddDotEnvSnapshot(dotEnvPath);
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<SpecSourceClient>();
builder.Services.AddHttpClient(ConnectionRegistry.PlainClientKey)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
        AllowAutoRedirect = false,
    })
    .ConfigureHttpClient(c => {
        c.Timeout = TimeSpan.FromSeconds(15);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    });
builder.Services.AddHttpClient(ConnectionRegistry.SslSkipClientKey)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    })
    .ConfigureHttpClient(c => {
        c.Timeout = TimeSpan.FromSeconds(15);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    });
builder.Services
    .AddMcpServer(
        o => o.ServerInfo = new() {
            Name = "mcp-api-contract",
            Version = "1.0.0"
        }
    )
    .WithHttpTransport()
    .WithTools<ApiContractTools>();

WebApplication app = builder.Build();
app.UseMcpAccessToken();
app.MapMcpHealth("mcp-api-contract");
app.MapConfigStatus<ConnectionRegistry>("mcp-api-contract");
app.MapMcp("/mcp");
await app.RunAsync();
