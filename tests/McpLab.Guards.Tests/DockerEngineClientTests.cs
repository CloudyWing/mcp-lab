using System.Net;
using System.Text;
using CloudyWing.McpLab.Docker;
using CloudyWing.McpLab.Shared;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class DockerEngineClientTests {
    private const string MaxResponseBytesKey = "MCPLAB_MAX_RESPONSE_BYTES";

    [TestCase("tcp://mcp-docker-proxy:2375", "http://mcp-docker-proxy:2375/")]
    [TestCase("http://localhost:2375", "http://localhost:2375/")]
    public void ParseDockerHost_TcpOrHttp_ReturnsHttpBaseAddress(string value, string expected) {
        Assert.That(DockerEngineClient.ParseDockerHost(value)?.ToString(), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("unix:///var/run/docker.sock")]
    public void ParseDockerHost_EmptyOrUnixSocket_ReturnsNull(string? value) {
        Assert.That(DockerEngineClient.ParseDockerHost(value), Is.Null);
    }

    [Test]
    public void ParseDockerHost_UnsupportedScheme_Throws() {
        Assert.Throws<InvalidOperationException>(() => DockerEngineClient.ParseDockerHost("ssh://host"));
    }

    [Test]
    public void ServiceProvider_RegisteredWithSnapshotProvider_ResolvesClient() {
        IDotEnvSnapshotProvider snapshot = Substitute.For<IDotEnvSnapshotProvider>();
        snapshot.Current.Returns(new Dictionary<string, string> { [MaxResponseBytesKey] = "2048" });
        ServiceCollection services = new();
        services.AddSingleton(snapshot);
        services.AddSingleton<DockerEngineClient>();
        using ServiceProvider provider = services.BuildServiceProvider();

        DockerEngineClient client = provider.GetRequiredService<DockerEngineClient>();

        Assert.That(client.MaxResponseBytes, Is.EqualTo(2048));
    }

    [Test]
    public void MaxResponseBytes_NoSnapshot_ReturnsDefault() {
        DockerEngineClient client = new(new HttpClient(new StubHandler(HttpStatusCode.OK, "")));

        Assert.That(client.MaxResponseBytes, Is.EqualTo(1048576));
    }

    [Test]
    public void MaxResponseBytes_SnapshotChanges_ReturnsNewValueWithoutRecreatingClient() {
        IDotEnvSnapshotProvider snapshot = Substitute.For<IDotEnvSnapshotProvider>();
        snapshot.Current.Returns(new Dictionary<string, string> { [MaxResponseBytesKey] = "100" });
        DockerEngineClient client = CreateClient(new StubHandler(HttpStatusCode.OK, ""), snapshot);

        snapshot.Current.Returns(new Dictionary<string, string> { [MaxResponseBytesKey] = "300" });

        Assert.That(client.MaxResponseBytes, Is.EqualTo(300));
    }

    [Test]
    public async Task GetObjectAsync_BodyWithinLimit_ReturnsParsedObject() {
        DockerEngineClient client = CreateClient(new StubHandler(HttpStatusCode.OK, """{"Version":"27.0"}"""), 1024);

        System.Text.Json.Nodes.JsonObject result = await client.GetObjectAsync("/version");

        Assert.That(result["Version"]?.ToString(), Is.EqualTo("27.0"));
    }

    [Test]
    public async Task GetStringAsync_BodyExactlyAtLimit_ReturnsFullText() {
        DockerEngineClient client = CreateClient(new StubHandler(HttpStatusCode.OK, "0123456789"), 10);

        string text = await client.GetStringAsync("/_ping");

        Assert.That(text, Is.EqualTo("0123456789"));
    }

    [Test]
    public void GetObjectAsync_BodyOverLimit_ThrowsResponseTooLargeException() {
        string body = "{\"Env\":\"" + new string('x', 200) + "\"}";
        DockerEngineClient client = CreateClient(new StubHandler(HttpStatusCode.OK, body), 64);

        ResponseTooLargeException? exception = Assert.ThrowsAsync<ResponseTooLargeException>(
            async () => await client.GetObjectAsync("/containers/web/json")
        );

        using (Assert.EnterMultipleScope()) {
            Assert.That(exception!.Message, Does.Contain(MaxResponseBytesKey));
            Assert.That(exception.MaxResponseBytes, Is.EqualTo(64));
            Assert.That(exception.OriginalBytes, Is.EqualTo(Encoding.UTF8.GetByteCount(body)));
        }
    }

    [Test]
    public void GetArrayAsync_BodyOverLimit_ThrowsResponseTooLargeException() {
        string body = "[" + string.Join(",", Enumerable.Repeat("\"container-name\"", 20)) + "]";
        DockerEngineClient client = CreateClient(new StubHandler(HttpStatusCode.OK, body), 32);

        Assert.ThrowsAsync<ResponseTooLargeException>(async () => await client.GetArrayAsync("/containers/json"));
    }

    [Test]
    public async Task GetBoundedAsync_BodyOverLimit_ReturnsTruncatedBody() {
        string body = new('a', 500);
        DockerEngineClient client = CreateClient(new StubHandler(HttpStatusCode.OK, body), 100);

        BoundedBody result = await client.GetBoundedAsync("/containers/web/logs");

        using (Assert.EnterMultipleScope()) {
            Assert.That(result.Truncated, Is.True);
            Assert.That(result.Bytes, Has.Length.EqualTo(100));
            Assert.That(result.OriginalBytes, Is.EqualTo(500));
            Assert.That(result.OriginalBytesExact, Is.True);
        }
    }

    [Test]
    public async Task GetBoundedAsync_BodyWithinLimit_ReturnsCompleteBody() {
        DockerEngineClient client = CreateClient(new StubHandler(HttpStatusCode.OK, "short log"), 100);

        BoundedBody result = await client.GetBoundedAsync("/containers/web/logs");

        using (Assert.EnterMultipleScope()) {
            Assert.That(result.Truncated, Is.False);
            Assert.That(result.ToText(), Is.EqualTo("short log"));
        }
    }

    [Test]
    public void GetStringAsync_ErrorStatusWithBody_ThrowsWithBodyText() {
        DockerEngineClient client = CreateClient(
            new StubHandler(HttpStatusCode.NotFound, """{"message":"No such container: web"}"""),
            1024
        );

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.GetStringAsync("/containers/web/json")
        );

        Assert.That(exception!.Message, Is.EqualTo("""Docker Engine returned {"message":"No such container: web"}"""));
    }

    [Test]
    public void GetStringAsync_ErrorStatusWithoutBody_ThrowsWithStatusText() {
        DockerEngineClient client = CreateClient(new StubHandler(HttpStatusCode.InternalServerError, ""), 1024);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.GetStringAsync("/version")
        );

        Assert.That(exception!.Message, Is.EqualTo("Docker Engine returned 500 InternalServerError"));
    }

    private static DockerEngineClient CreateClient(HttpMessageHandler handler, int maxResponseBytes) {
        IDotEnvSnapshotProvider snapshot = Substitute.For<IDotEnvSnapshotProvider>();
        snapshot.Current.Returns(new Dictionary<string, string> {
            [MaxResponseBytesKey] = maxResponseBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });

        return CreateClient(handler, snapshot);
    }

    private static DockerEngineClient CreateClient(HttpMessageHandler handler, IDotEnvSnapshotProvider snapshot) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://docker") }, snapshot);

    private sealed class StubHandler : HttpMessageHandler {
        private readonly HttpStatusCode statusCode;
        private readonly string body;

        public StubHandler(HttpStatusCode statusCode, string body) {
            this.statusCode = statusCode;
            this.body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }
}
