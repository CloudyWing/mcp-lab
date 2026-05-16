using CloudyWing.McpLab.ApiContract;
using CloudyWing.McpLab.Shared;

string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<SpecSourceClient>();
builder.Services.AddHttpClient(ConnectionRegistry.PlainClientKey)
    .ConfigureHttpClient(c => {
        c.Timeout = TimeSpan.FromSeconds(15);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    });
builder.Services.AddHttpClient(ConnectionRegistry.SslSkipClientKey)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
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
app.MapMcpHealth("mcp-api-contract");
app.MapMcp("/mcp");
await app.RunAsync();
