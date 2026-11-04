using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CloudyWing.McpLab.Admin.Services;

/// <summary>
/// Result of testing a connection.
/// </summary>
/// <param name="Success">Whether the service reported success.</param>
/// <param name="Message">Message from the service's tool response.</param>
public sealed record ConnectionTestResult(bool Success, string Message);

/// <summary>
/// Tests a connection by calling the service's own test tool over MCP, so the admin needs no database drivers.
/// </summary>
public sealed class ConnectionTester {
    private readonly ConfigStore store;
    private readonly ServiceEndpoints endpoints;

    /// <summary>
    /// Initializes a new instance of <see cref="ConnectionTester"/>.
    /// </summary>
    public ConnectionTester(ConfigStore store, ServiceEndpoints endpoints) {
        this.store = store;
        this.endpoints = endpoints;
    }

    /// <summary>
    /// Calls the test tool of <paramref name="service"/> for a connection name.
    /// </summary>
    public async Task<ConnectionTestResult> TestAsync(ManagedService service, string connectionName, CancellationToken cancellationToken = default) {
        if (service.TestTool is null) {
            return new(false, "此服務沒有連線測試工具。");
        }

        try {
            Dictionary<string, string> headers = [];
            string token = endpoints.TokenFor(store, service.Name);

            if (token.Length > 0) {
                headers["Authorization"] = $"Bearer {token}";
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            HttpClientTransport transport = new(new HttpClientTransportOptions {
                Endpoint = new Uri(endpoints.BaseUrl(service.Name) + "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = headers,
            });
            await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
            CallToolResult result = await client.CallToolAsync(
                service.TestTool,
                new Dictionary<string, object?> { ["connection"] = connectionName },
                cancellationToken: timeout.Token
            ).ConfigureAwait(false);

            string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

            return Interpret(text);
        } catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
            return new(false, $"無法呼叫服務：{ex.Message}");
        }
    }

    /// <summary>
    /// Reads the <c>ok</c> and <c>message</c> fields of a tool response envelope.
    /// </summary>
    public static ConnectionTestResult Interpret(string toolText) {
        try {
            using JsonDocument document = JsonDocument.Parse(toolText);
            JsonElement root = document.RootElement;
            bool ok = root.TryGetProperty("ok", out JsonElement okElement) && okElement.ValueKind == JsonValueKind.True;
            string message = root.TryGetProperty("message", out JsonElement messageElement) ? messageElement.GetString() ?? "" : "";

            return new(ok, ok ? "連線成功。" : message);
        } catch (JsonException) {
            return new(false, toolText);
        }
    }
}
