using System.Net;
using CloudyWing.McpLab.Oidc;
using CloudyWing.McpLab.Shared;
using CloudyWing.McpLab.Shared.DotEnv;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class OidcDocumentCacheTests {
    private const string Discovery = """{"issuer":"http://idp","jwks_uri":"http://idp/jwks"}""";
    private const string Jwks = """{"keys":[{"kty":"RSA","kid":"k1","n":"AQAB","e":"AQAB"}]}""";

    [Test]
    public async Task GetJsonWebKeysAsync_WithinCacheTtl_FetchesOnce() {
        CountingHandler handler = new();
        OidcDocumentClient client = CreateClient(handler, new() { ["OIDC_METADATA_CACHE_SECONDS"] = "300" });

        await client.GetJsonWebKeysAsync(Config());
        await client.GetJsonWebKeysAsync(Config());

        Assert.That(handler.Requests, Is.EqualTo(2), "one discovery and one JWKS request");
    }

    [Test]
    public async Task GetJsonWebKeysAsync_ForceRefreshOrCacheDisabled_FetchesAgain() {
        CountingHandler forced = new();
        OidcDocumentClient forcedClient = CreateClient(forced, []);
        await forcedClient.GetJsonWebKeysAsync(Config());
        await forcedClient.GetJsonWebKeysAsync(Config(), forceRefresh: true);

        CountingHandler disabled = new();
        OidcDocumentClient disabledClient = CreateClient(disabled, new() { ["OIDC_METADATA_CACHE_SECONDS"] = "0" });
        await disabledClient.GetJsonWebKeysAsync(Config());
        await disabledClient.GetJsonWebKeysAsync(Config());

        Assert.Multiple(() => {
            Assert.That(forced.Requests, Is.EqualTo(4));
            Assert.That(disabled.Requests, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task GetJsonWebKeysAsync_RepeatedForceRefresh_RefetchesOncePerInterval() {
        CountingHandler handler = new();
        OidcDocumentClient client = CreateClient(handler, []);
        await client.GetJsonWebKeysAsync(Config());

        await client.GetJsonWebKeysAsync(Config(), forceRefresh: true);
        await client.GetJsonWebKeysAsync(Config(), forceRefresh: true);
        await client.GetJsonWebKeysAsync(Config(), forceRefresh: true);

        Assert.That(handler.Requests, Is.EqualTo(4), "initial load plus one forced refresh");
    }

    [Test]
    public void NeedsKeyRefresh_UnknownKid_ReturnsTrue() {
        string token = "eyJhbGciOiJSUzI1NiIsImtpZCI6Im5ldyJ9.eyJzdWIiOiIxIn0.c2ln";
        JsonWebKey known = new() { Kid = "old" };

        Assert.Multiple(() => {
            Assert.That(OidcTools.NeedsKeyRefresh(token, [known]), Is.True);
            Assert.That(OidcTools.NeedsKeyRefresh(token, [new JsonWebKey { Kid = "new" }]), Is.False);
            Assert.That(OidcTools.NeedsKeyRefresh("not-a-jwt", [known]), Is.False);
        });
    }

    [Test]
    public async Task GetJsonWebKeysAsync_BodyOverLimit_ThrowsAndDoesNotCache() {
        CountingHandler handler = new();
        IDotEnvSnapshotProvider snapshot = Substitute.For<IDotEnvSnapshotProvider>();
        snapshot.Current.Returns(new Dictionary<string, string> { ["MCPLAB_MAX_RESPONSE_BYTES"] = "20" });
        OidcDocumentClient client = CreateClientWithSnapshot(handler, snapshot);

        ResponseTooLargeException? exception = Assert.ThrowsAsync<ResponseTooLargeException>(
            async () => await client.GetJsonWebKeysAsync(Config())
        );
        int requestsAfterFailure = handler.Requests;
        snapshot.Current.Returns(new Dictionary<string, string> { ["MCPLAB_MAX_RESPONSE_BYTES"] = "4096" });
        IReadOnlyList<JsonWebKey> keys = await client.GetJsonWebKeysAsync(Config());

        using (Assert.EnterMultipleScope()) {
            Assert.That(exception!.Message, Does.Contain("MCPLAB_MAX_RESPONSE_BYTES"));
            Assert.That(exception.MaxResponseBytes, Is.EqualTo(20));
            Assert.That(requestsAfterFailure, Is.EqualTo(1));
            Assert.That(handler.Requests, Is.EqualTo(3), "failed discovery was not cached, so it is fetched again");
            Assert.That(keys, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void GetDiscoveryDocumentAsync_ErrorStatus_ThrowsWithTruncatedBodyText() {
        string body = new('e', 800);
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(
            _ => new HttpClient(new FixedHandler(HttpStatusCode.BadGateway, body), disposeHandler: false)
        );
        OidcDocumentClient client = new(factory);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.GetDiscoveryDocumentAsync(Config())
        );

        Assert.That(exception!.Message, Is.EqualTo($"OIDC metadata endpoint returned HTTP 502: {new string('e', 500)}"));
    }

    private static ConnectionConfig Config() => new("idp", "http://idp", "http://idp/.well-known/openid-configuration", "", false);

    private static OidcDocumentClient CreateClient(CountingHandler handler, Dictionary<string, string> settings) {
        IDotEnvSnapshotProvider snapshot = Substitute.For<IDotEnvSnapshotProvider>();
        snapshot.Current.Returns(settings);

        return CreateClientWithSnapshot(handler, snapshot);
    }

    private static OidcDocumentClient CreateClientWithSnapshot(CountingHandler handler, IDotEnvSnapshotProvider snapshot) {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        return new OidcDocumentClient(factory, snapshot);
    }

    private sealed class FixedHandler : HttpMessageHandler {
        private readonly HttpStatusCode statusCode;
        private readonly string body;

        public FixedHandler(HttpStatusCode statusCode, string body) {
            this.statusCode = statusCode;
            this.body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
    }

    private sealed class CountingHandler : HttpMessageHandler {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests++;
            string body = request.RequestUri!.AbsolutePath.EndsWith("jwks", StringComparison.Ordinal) ? Jwks : Discovery;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
