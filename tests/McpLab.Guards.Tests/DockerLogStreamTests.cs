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
