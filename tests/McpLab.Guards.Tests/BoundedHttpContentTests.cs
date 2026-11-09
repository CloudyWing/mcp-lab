using CloudyWing.McpLab.Shared;

using NUnit.Framework;

namespace CloudyWing.McpLab.Guards.Tests;

internal sealed class BoundedHttpContentTests {
    [Test]
    public async Task ReadAsync_BodyLongerThanLimit_ReturnsLimitAndTruncated() {
        using ByteArrayContent content = new(new byte[5000]);

        BoundedBody body = await BoundedHttpContent.ReadAsync(content, 1000);

        Assert.Multiple(() => {
            Assert.That(body.Bytes, Has.Length.EqualTo(1000));
            Assert.That(body.Truncated, Is.True);
        });
    }

    [TestCase(10)]
    [TestCase(1000)]
    public async Task ReadAsync_BodyWithinLimit_ReturnsWholeBody(int length) {
        using ByteArrayContent content = new(new byte[length]);

        BoundedBody body = await BoundedHttpContent.ReadAsync(content, 1000);

        Assert.Multiple(() => {
            Assert.That(body.Bytes, Has.Length.EqualTo(length));
            Assert.That(body.Truncated, Is.False);
            Assert.That(body.OriginalBytes, Is.EqualTo(length));
            Assert.That(body.OriginalBytesExact, Is.True);
        });
    }

    [Test]
    public async Task ReadAsync_EmptyBody_ReturnsEmptyBody() {
        using ByteArrayContent content = new([]);

        BoundedBody body = await BoundedHttpContent.ReadAsync(content, 1000);

        Assert.Multiple(() => {
            Assert.That(body.Bytes, Is.Empty);
            Assert.That(body.Truncated, Is.False);
            Assert.That(body.OriginalBytes, Is.Zero);
        });
    }

    [Test]
    public async Task ReadAsync_TruncatedWithContentLength_ReportsExactOriginalBytes() {
        using ByteArrayContent content = new(new byte[5000]);

        BoundedBody body = await BoundedHttpContent.ReadAsync(content, 1000);

        Assert.Multiple(() => {
            Assert.That(body.OriginalBytes, Is.EqualTo(5000));
            Assert.That(body.OriginalBytesExact, Is.True);
        });
    }

    [Test]
    public async Task ReadAsync_TruncatedWithoutContentLength_ReportsBytesReadAsInexact() {
        using StreamContent content = new(new NonSeekableStream(new byte[100_000]));

        BoundedBody body = await BoundedHttpContent.ReadAsync(content, 1000);

        Assert.Multiple(() => {
            Assert.That(content.Headers.ContentLength, Is.Null);
            Assert.That(body.Bytes, Has.Length.EqualTo(1000));
            Assert.That(body.Truncated, Is.True);
            Assert.That(body.OriginalBytes, Is.EqualTo(1001));
            Assert.That(body.OriginalBytesExact, Is.False);
        });
    }

    [Test]
    public async Task ReadAsync_ChunkedBodyLargerThanOneChunkWithinLimit_ReturnsWholeBody() {
        byte[] source = new byte[50_000];
        Random.Shared.NextBytes(source);
        using StreamContent content = new(new NonSeekableStream(source));

        BoundedBody body = await BoundedHttpContent.ReadAsync(content, 100_000);

        Assert.Multiple(() => {
            Assert.That(body.Truncated, Is.False);
            Assert.That(body.Bytes, Is.EqualTo(source));
            Assert.That(body.OriginalBytes, Is.EqualTo(source.Length));
        });
    }

    [Test]
    public async Task ReadAsync_HugeLimitWithSmallBody_DoesNotPreallocateLimit() {
        using ByteArrayContent content = new(new byte[10]);

        BoundedBody body = await BoundedHttpContent.ReadAsync(content, int.MaxValue / 2);

        Assert.Multiple(() => {
            Assert.That(body.Bytes, Has.Length.EqualTo(10));
            Assert.That(body.Truncated, Is.False);
        });
    }

    [Test]
    public async Task ReadAsync_HugeLimitWithSmallBodyWithoutContentLength_DoesNotPreallocateLimit() {
        using StreamContent content = new(new NonSeekableStream(new byte[10]));

        BoundedBody body = await BoundedHttpContent.ReadAsync(content, int.MaxValue / 2);

        Assert.That(body.Bytes, Has.Length.EqualTo(10));
    }

    [Test]
    public async Task ReadAsync_ExactContentLengthWithinLimit_ReturnsWholeBodyWithoutGrowingBuffer() {
        byte[] source = new byte[600_000];
        Random.Shared.NextBytes(source);
        using ByteArrayContent content = new(source);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        BoundedBody body = await BoundedHttpContent.ReadAsync(content, 1_048_576);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        using (Assert.EnterMultipleScope()) {
            Assert.That(content.Headers.ContentLength, Is.EqualTo(source.Length));
            Assert.That(body.Bytes, Is.EqualTo(source));
            Assert.That(body.Truncated, Is.False);
            Assert.That(body.OriginalBytes, Is.EqualTo(source.Length));
            Assert.That(body.OriginalBytesExact, Is.True);
            Assert.That(allocated, Is.LessThan(source.Length * 3L / 2));
        }
    }

    [Test]
    public async Task ReadAsync_BodyLongerThanDeclaredContentLength_ReadsWholeBodyWithinLimit() {
        byte[] source = new byte[2000];
        Random.Shared.NextBytes(source);
        using StreamContent content = new(new NonSeekableStream(source));
        content.Headers.ContentLength = 1000;

        BoundedBody body = await BoundedHttpContent.ReadAsync(content, 5000);

        using (Assert.EnterMultipleScope()) {
            Assert.That(body.Bytes, Is.EqualTo(source));
            Assert.That(body.Truncated, Is.False);
            Assert.That(body.OriginalBytes, Is.EqualTo(source.Length));
        }
    }

    [Test]
    public async Task ReadAsync_TruncatedBody_DoesNotDrainRemainingStream() {
        NonSeekableStream stream = new(new byte[1_000_000]);
        using StreamContent content = new(stream);

        await BoundedHttpContent.ReadAsync(content, 1000);

        Assert.That(stream.TotalRead, Is.LessThan(100_000));
    }

    [Test]
    public async Task GetAsync_BodyLongerThanLimit_DoesNotBufferWholeBody() {
        NonSeekableStream stream = new(new byte[1_000_000]);
        using HttpClient http = new(new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
            Content = new StreamContent(stream),
        })) { BaseAddress = new Uri("http://test.invalid/") };

        using BoundedResponse response = await BoundedHttpContent.GetAsync(http, "x", 1000);

        Assert.Multiple(() => {
            Assert.That(stream.TotalRead, Is.LessThan(100_000));
            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(response.Body.Truncated, Is.True);
            Assert.That(response.Body.Bytes, Has.Length.EqualTo(1000));
        });
    }

    [Test]
    public async Task SendAsync_NonSuccessStatus_ReturnsBodyWithoutThrowing() {
        using HttpClient http = new(new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) {
            Content = new StringContent("bad"),
        }));
        using HttpRequestMessage request = new(HttpMethod.Get, "http://test.invalid/x");

        using BoundedResponse response = await BoundedHttpContent.SendAsync(http, request, 1000);

        Assert.Multiple(() => {
            Assert.That(response.IsSuccessStatusCode, Is.False);
            Assert.That(response.Body.ToText(), Is.EqualTo("bad"));
        });
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(respond(request));
    }

    private sealed class NonSeekableStream(byte[] data) : Stream {
        private int position;

        public long TotalRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) {
            int count = Math.Min(Math.Min(buffer.Length, 8192), data.Length - position);
            data.AsSpan(position, count).CopyTo(buffer);
            position += count;
            TotalRead += count;

            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Read(buffer.Span));

        public override void Flush() {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
