using CloudyWing.McpLab.Shared;

using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class BoundedHttpContentTests {
    [Test]
    public async Task ReadAsync_BodyLongerThanLimit_ReturnsLimitAndTruncated() {
        using ByteArrayContent content = new(new byte[5000]);

        (byte[] bytes, bool truncated) = await BoundedHttpContent.ReadAsync(content, 1000);

        Assert.Multiple(() => {
            Assert.That(bytes, Has.Length.EqualTo(1000));
            Assert.That(truncated, Is.True);
        });
    }

    [TestCase(10)]
    [TestCase(1000)]
    public async Task ReadAsync_BodyWithinLimit_ReturnsWholeBody(int length) {
        using ByteArrayContent content = new(new byte[length]);

        (byte[] bytes, bool truncated) = await BoundedHttpContent.ReadAsync(content, 1000);

        Assert.Multiple(() => {
            Assert.That(bytes, Has.Length.EqualTo(length));
            Assert.That(truncated, Is.False);
        });
    }
}
