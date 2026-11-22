using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using a2Cr.Core.Download;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Tests.Download;

public sealed class DownloadServiceTests
{
    [Fact]
    public async Task Download_ResumesPart_WhenServerReturns206()
    {
        var directory = CreateDirectory();
        var target = IoPath.Combine(directory, "sample.bin");
        var content = "完整的下载内容"u8.ToArray();
        await File.WriteAllBytesAsync(target + ".part", content[..4]);
        var handler = new RecordingHandler(content, supportsRange: true);
        var service = new DownloadService(new HttpClient(handler));

        var report = await service.DownloadAsync([
            new DownloadItem("https://test.invalid/file", target, Sha1(content), content.Length)
        ]);

        Assert.True(report.AllSucceeded, string.Join("; ", report.Failed.Select(item => item.Reason)));
        Assert.Equal(content, await File.ReadAllBytesAsync(target));
        Assert.Equal(1, handler.Requests);
        Assert.Equal(new RangeHeaderValue(4, null).ToString(), handler.LastRange);
    }

    [Fact]
    public async Task Download_OverwritesPart_WhenServerIgnoresRange()
    {
        var directory = CreateDirectory();
        var target = IoPath.Combine(directory, "sample.bin");
        var content = "服务器从头返回"u8.ToArray();
        await File.WriteAllBytesAsync(target + ".part", "错误分片"u8.ToArray());
        var service = new DownloadService(new HttpClient(new RecordingHandler(content, supportsRange: false)));

        var report = await service.DownloadAsync([
            new DownloadItem("https://test.invalid/file", target, Sha1(content), content.Length)
        ]);

        Assert.True(report.AllSucceeded, string.Join("; ", report.Failed.Select(item => item.Reason)));
        Assert.Equal(content, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Download_404_DoesNotRetry()
    {
        var directory = CreateDirectory();
        var handler = new RecordingHandler([], statusCode: HttpStatusCode.NotFound);
        var service = new DownloadService(new HttpClient(handler));

        var report = await service.DownloadAsync([
            new DownloadItem("https://test.invalid/missing", IoPath.Combine(directory, "missing"), null, 0)
        ], new DownloadOptions { MaxRetry = 4 });

        Assert.False(report.AllSucceeded);
        Assert.Single(report.Failed);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Download_Cancellation_LeavesPartFile()
    {
        var directory = CreateDirectory();
        var target = IoPath.Combine(directory, "sample.bin");
        using var cancellation = new CancellationTokenSource();
        var handler = new BlockingHandler();
        var service = new DownloadService(new HttpClient(handler));

        var task = service.DownloadAsync([
            new DownloadItem("https://test.invalid/file", target, null, 0)
        ], cancellationToken: cancellation.Token);
        cancellation.CancelAfter(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.True(File.Exists(target + ".part"));
    }

    private static string CreateDirectory()
    {
        var directory = IoPath.Combine(IoPath.GetTempPath(), "a2cr-download-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Sha1(byte[] content) => Convert.ToHexString(SHA1.HashData(content));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly bool _supportsRange;
        private readonly HttpStatusCode _statusCode;

        public RecordingHandler(byte[] content, bool supportsRange = true, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _content = content;
            _supportsRange = supportsRange;
            _statusCode = statusCode;
        }

        public int Requests { get; private set; }
        public string? LastRange { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastRange = request.Headers.Range?.ToString();
            if (_statusCode != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(_statusCode));
            }

            var responseStatus = _supportsRange && request.Headers.Range is not null
                ? HttpStatusCode.PartialContent
                : HttpStatusCode.OK;
            var offset = responseStatus == HttpStatusCode.PartialContent ? (int)request.Headers.Range!.Ranges.Single().From!.Value : 0;
            var response = new HttpResponseMessage(responseStatus)
            {
                Content = new ByteArrayContent(_content[offset..])
            };
            return Task.FromResult(response);
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new CancelableStream(cancellationToken))
            };
            await Task.Yield();
            return response;
        }
    }

    private sealed class CancelableStream : Stream
    {
        private readonly CancellationToken _token;

        public CancelableStream(CancellationToken token)
        {
            _token = token;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 1;
        public override long Position { get; set; }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return WaitAsync(cancellationToken);
        }

        private CancellationToken _cancellationToken => _token;

        private static async ValueTask<int> WaitAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
