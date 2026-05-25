using CloudyWing.McpLab.Shared.DotEnv;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class DotEnvParserTests {
    [Test]
    public void Parse_SupportedSyntax_ReturnsExpectedValues() {
        string content =
            "\uFEFF# comment\n" +
            "KEY=value\n" +
            "EMPTY=\n" +
            "QUOTED=\"line\\nvalue\\twith \\\"quotes\\\" and \\\\ slash\"\n" +
            "SINGLE='literal\\nvalue'\n" +
            "export EXPORTED=ok\n" +
            "INLINE=value # not comment\n" +
            "INTERPOLATION=${OTHER}\n";

        IReadOnlyDictionary<string, string> actual = DotEnvParser.Parse(content);

        using (Assert.EnterMultipleScope()) {
            Assert.That(actual["KEY"], Is.EqualTo("value"));
            Assert.That(actual["EMPTY"], Is.EqualTo(""));
            Assert.That(actual["QUOTED"], Is.EqualTo("line\nvalue\twith \"quotes\" and \\ slash"));
            Assert.That(actual["SINGLE"], Is.EqualTo("literal\\nvalue"));
            Assert.That(actual["EXPORTED"], Is.EqualTo("ok"));
            Assert.That(actual["INLINE"], Is.EqualTo("value # not comment"));
            Assert.That(actual["INTERPOLATION"], Is.EqualTo("${OTHER}"));
        }
    }

    [Test]
    public void Parse_DuplicateKeys_UsesLastValue() {
        string content = """
            KEY=first
            KEY=second
            """;

        IReadOnlyDictionary<string, string> actual = DotEnvParser.Parse(content);

        Assert.That(actual["KEY"], Is.EqualTo("second"));
    }

    [TestCase("MISSING_SEPARATOR")]
    [TestCase("1INVALID=value")]
    [TestCase("KEY=\"unterminated")]
    [TestCase("KEY='unterminated")]
    public void Parse_InvalidSyntax_ThrowsFormatException(string content) {
        FormatException exception = Assert.Throws<FormatException>(
            () => DotEnvParser.Parse(content)
        )!;

        Assert.That(exception.Message, Does.Contain("line 1"));
    }
}
