using System.Text;
using CloudyWing.McpLab.Docker;
using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class DockerLogStreamTests {
    [Test]
    public void Decode_RawTtyBytes_ReturnsUtf8Text() {
        byte[] bytes = Encoding.UTF8.GetBytes("line 1\nline 2\n");

        string actual = DockerLogStream.Decode(bytes);

        Assert.That(actual, Is.EqualTo("line 1\nline 2\n"));
    }

    [Test]
    public void Decode_MultiplexedFrames_ReturnsMergedText() {
        byte[] stdout = CreateFrame(1, "out\n");
        byte[] stderr = CreateFrame(2, "err\n");
        byte[] bytes = stdout.Concat(stderr).ToArray();

        string actual = DockerLogStream.Decode(bytes);

        Assert.That(actual, Is.EqualTo("out\nerr\n"));
    }

    [Test]
    public void Decode_InvalidMultiplexedFrame_ReturnsRawText() {
        byte[] bytes = [1, 0, 0, 0, 0, 0, 0, 10, 65];

        string actual = DockerLogStream.Decode(bytes);

        Assert.That(actual, Is.EqualTo(Encoding.UTF8.GetString(bytes)));
    }

    [Test]
    public void Decode_PartialWithFrameCutInPayload_ReturnsAvailableBytes() {
        byte[] complete = CreateFrame(1, "out\n");
        byte[] cut = CreateFrame(2, "error line\n").Take(8 + 5).ToArray();
        byte[] bytes = complete.Concat(cut).ToArray();

        string actual = DockerLogStream.Decode(bytes, partial: true);

        Assert.That(actual, Is.EqualTo("out\nerror"));
    }

    [Test]
    public void Decode_PartialWithFrameCutInHeader_ReturnsCompleteFramesOnly() {
        byte[] complete = CreateFrame(1, "out\n");
        byte[] cut = CreateFrame(2, "error line\n").Take(3).ToArray();
        byte[] bytes = complete.Concat(cut).ToArray();

        string actual = DockerLogStream.Decode(bytes, partial: true);

        Assert.That(actual, Is.EqualTo("out\n"));
    }

    [Test]
    public void Decode_PartialWithFirstFrameCutInPayload_ReturnsAvailableBytes() {
        byte[] bytes = CreateFrame(1, "first line\n").Take(8 + 5).ToArray();

        string actual = DockerLogStream.Decode(bytes, partial: true);

        Assert.That(actual, Is.EqualTo("first"));
    }

    [Test]
    public void Decode_PartialWithFirstHeaderCut_ReturnsEmptyText() {
        byte[] bytes = CreateFrame(1, "first line\n").Take(5).ToArray();

        string actual = DockerLogStream.Decode(bytes, partial: true);

        Assert.That(actual, Is.Empty);
    }

    [Test]
    public void Decode_PartialWithCompleteFrames_MatchesNonPartialResult() {
        byte[] bytes = CreateFrame(1, "out\n").Concat(CreateFrame(2, "err\n")).ToArray();

        string actual = DockerLogStream.Decode(bytes, partial: true);

        Assert.That(actual, Is.EqualTo(DockerLogStream.Decode(bytes)));
    }

    [Test]
    public void Decode_PartialWithMultibyteCharacterCut_DropsIncompleteSequence() {
        byte[] frame = CreateFrame(1, "中文");
        byte[] bytes = frame.Take(frame.Length - 1).ToArray();

        string actual = DockerLogStream.Decode(bytes, partial: true);

        Assert.That(actual, Is.EqualTo("中"));
    }

    [Test]
    public void Decode_PartialRawTextCutInMultibyteCharacter_DropsIncompleteSequence() {
        byte[] full = Encoding.UTF8.GetBytes("tty 中文");
        byte[] bytes = full.Take(full.Length - 1).ToArray();

        string actual = DockerLogStream.Decode(bytes, partial: true);

        Assert.That(actual, Is.EqualTo("tty 中"));
    }

    [Test]
    public void Decode_NotPartialWithFrameCutInPayload_FallsBackToRawText() {
        byte[] bytes = CreateFrame(1, "error line\n").Take(8 + 5).ToArray();

        string actual = DockerLogStream.Decode(bytes, partial: false);

        Assert.That(actual, Is.EqualTo(Encoding.UTF8.GetString(bytes)));
    }

    private static byte[] CreateFrame(int streamType, string text) {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        byte[] frame = new byte[8 + payload.Length];
        frame[0] = (byte)streamType;
        frame[4] = (byte)((payload.Length >> 24) & 0xff);
        frame[5] = (byte)((payload.Length >> 16) & 0xff);
        frame[6] = (byte)((payload.Length >> 8) & 0xff);
        frame[7] = (byte)(payload.Length & 0xff);
        Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);

        return frame;
    }
}
