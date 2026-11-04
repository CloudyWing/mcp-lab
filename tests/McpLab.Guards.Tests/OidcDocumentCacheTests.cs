using System.Net;
using CloudyWing.McpLab.Oidc;
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
    public void NeedsKeyRefresh_UnknownKid_ReturnsTrue() {
        string token = "eyJhbGciOiJSUzI1NiIsImtpZCI6Im5ldyJ9.eyJzdWIiOiIxIn0.c2ln";
        JsonWebKey known = new() { Kid = "old" };

        Assert.Multiple(() => {
            Assert.That(OidcTools.NeedsKeyRefresh(token, [known]), Is.True);
            Assert.That(OidcTools.NeedsKeyRefresh(token, [new JsonWebKey { Kid = "new" }]), Is.False);
            Assert.That(OidcTools.NeedsKeyRefresh("not-a-jwt", [known]), Is.False);
        });
    }

    private static ConnectionConfig Config() => new("idp", "http://idp", "http://idp/.well-known/openid-configuration", "", false);

    private static OidcDocumentClient CreateClient(CountingHandler handler, Dictionary<string, string> settings) {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        IDotEnvSnapshotProvider snapshot = Substitute.For<IDotEnvSnapshotProvider>();
        snapshot.Current.Returns(settings);

        return new OidcDocumentClient(factory, snapshot);
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
