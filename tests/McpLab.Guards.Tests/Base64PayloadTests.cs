using System.Text;
using CloudyWing.McpLab.Shared;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class Base64PayloadTests {
    [Test]
    public void TryDecodeUtf8_StandardBase64_ReturnsDecodedText() {
        string input = Convert.ToBase64String(Encoding.UTF8.GetBytes("SELECT 1"));

        bool actual = Base64Payload.TryDecodeUtf8(
            input,
            "payload empty",
            "decoded empty",
            out string value,
            out string error
        );

        using (Assert.EnterMultipleScope()) {
            Assert.That(actual, Is.True);
            Assert.That(value, Is.EqualTo("SELECT 1"));
            Assert.That(error, Is.EqualTo(""));
        }
    }

    [Test]
    public void TryDecodeUtf8_UrlSafeBase64WithoutPadding_ReturnsDecodedText() {
        string input = Convert.ToBase64String(Encoding.UTF8.GetBytes("??"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        bool actual = Base64Payload.TryDecodeUtf8(
            input,
            "payload empty",
            "decoded empty",
            out string value,
            out string error
        );

        using (Assert.EnterMultipleScope()) {
            Assert.That(actual, Is.True);
            Assert.That(value, Is.EqualTo("??"));
            Assert.That(error, Is.EqualTo(""));
        }
    }

    [Test]
    public void TryDecodeUtf8_EmptyInput_ReturnsConfiguredError() {
        bool actual = Base64Payload.TryDecodeUtf8(
            "",
            "payload empty",
            "decoded empty",
            out string value,
            out string error
        );

        using (Assert.EnterMultipleScope()) {
            Assert.That(actual, Is.False);
            Assert.That(value, Is.EqualTo(""));
            Assert.That(error, Is.EqualTo("payload empty"));
        }
    }

    [Test]
    public void TryDecodeUtf8_InvalidBase64_ReturnsInvalidBase64Error() {
        bool actual = Base64Payload.TryDecodeUtf8(
            "not base64",
            "payload empty",
            "decoded empty",
            out string value,
            out string error
        );

        using (Assert.EnterMultipleScope()) {
            Assert.That(actual, Is.False);
            Assert.That(value, Is.EqualTo(""));
            Assert.That(error, Is.EqualTo("Invalid Base64."));
        }
    }
}
