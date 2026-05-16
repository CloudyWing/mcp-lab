using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using CloudyWing.McpLab.Oidc;
using NSubstitute;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class OidcJwtInspectorTests {
    [Test]
    public void Normalize_BearerToken_RemovesScheme() {
        string token = JwtTokenInput.Normalize("Bearer header.payload.signature");

        Assert.That(token, Is.EqualTo("header.payload.signature"));
    }

    [Test]
    public void InspectJwt_ValidReadableToken_ReturnsClaimSummary() {
        string token = CreateUnsignedToken();
        OidcTools tools = new(
            new ConnectionRegistry(),
            new OidcDocumentClient(Substitute.For<IHttpClientFactory>())
        );

        string json = tools.InspectJwt($"Bearer {token}", includeClaims: true, claimLimit: 10);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement data = document.RootElement.GetProperty("data");
        JsonElement jwt = data.GetProperty("jwt");

        using (Assert.EnterMultipleScope()) {
            Assert.That(document.RootElement.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(data.GetProperty("signature_validated").GetBoolean(), Is.False);
            Assert.That(jwt.GetProperty("issuer").GetString(), Is.EqualTo("https://issuer.example.test"));
            Assert.That(jwt.GetProperty("subject").GetString(), Is.EqualTo("subject-1"));
            Assert.That(jwt.GetProperty("audiences")[0].GetString(), Is.EqualTo("api://default"));
            Assert.That(jwt.GetProperty("scopes")[0].GetString(), Is.EqualTo("openid"));
            Assert.That(jwt.GetProperty("claims").GetArrayLength(), Is.GreaterThan(0));
        }
    }

    [Test]
    public void InspectJwt_InvalidToken_ReturnsErrorEnvelope() {
        OidcTools tools = new(
            new ConnectionRegistry(),
            new OidcDocumentClient(Substitute.For<IHttpClientFactory>())
        );

        string json = tools.InspectJwt("not-a-jwt");

        using JsonDocument document = JsonDocument.Parse(json);

        using (Assert.EnterMultipleScope()) {
            Assert.That(document.RootElement.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(document.RootElement.GetProperty("kind").GetString(), Is.EqualTo("error"));
        }
    }

    private static string CreateUnsignedToken() {
        JwtSecurityToken token = new(
            issuer: "https://issuer.example.test",
            audience: "api://default",
            claims: [
                new Claim("sub", "subject-1"),
                new Claim("scope", "openid profile"),
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: null
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
