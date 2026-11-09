using System.Text;
using CloudyWing.McpLab.Shared;

using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class Utf8TextTests {
    [TestCase(1, "a")]
    [TestCase(2, "a")]
    [TestCase(3, "a")]
    [TestCase(4, "a中")]
    [TestCase(5, "a中")]
    [TestCase(6, "a中")]
    [TestCase(7, "a中文")]
    public void DecodePrefix_TruncatedInsideMultiByteCharacter_DropsIncompleteSequence(int length, string expected) {
        byte[] bytes = Encoding.UTF8.GetBytes("a中文");

        string text = Utf8Text.DecodePrefix(bytes.AsSpan(0, length), true);

        Assert.That(text, Is.EqualTo(expected));
    }

    [Test]
    public void DecodePrefix_TruncatedInsideFourByteCharacter_DropsIncompleteSequence() {
        byte[] bytes = Encoding.UTF8.GetBytes("a😀");

        string text = Utf8Text.DecodePrefix(bytes.AsSpan(0, 3), true);

        Assert.That(text, Is.EqualTo("a"));
    }

    [Test]
    public void DecodePrefix_TruncatedAtCharacterBoundary_KeepsEverything() {
        byte[] bytes = Encoding.UTF8.GetBytes("中文");

        string text = Utf8Text.DecodePrefix(bytes, true);

        Assert.That(text, Is.EqualTo("中文"));
    }

    [Test]
    public void DecodePrefix_NotTruncated_DoesNotDropTrailingBytes() {
        byte[] bytes = Encoding.UTF8.GetBytes("a中").AsSpan(0, 3).ToArray();

        string text = Utf8Text.DecodePrefix(bytes, false);

        Assert.That(text, Is.EqualTo("a�"));
    }

    [Test]
    public void DecodePrefix_EmptyBytes_ReturnsEmpty() {
        Assert.Multiple(() => {
            Assert.That(Utf8Text.DecodePrefix([], true), Is.Empty);
            Assert.That(Utf8Text.DecodePrefix([], false), Is.Empty);
        });
    }
}
