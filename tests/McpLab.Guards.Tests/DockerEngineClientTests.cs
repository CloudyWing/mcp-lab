using CloudyWing.McpLab.Docker;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class DockerEngineClientTests {
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
}
