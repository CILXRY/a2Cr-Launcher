using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using a2Cr.Core.Launcher.Definitions;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Download;

public sealed record DownloadOptions
{
    public int MaxConcurrency { get; init; } = 8;
    public int MaxRetry { get; init; } = 4;
    public long MaxBytesPerSecond { get; init; }
    public string? ProxyUrl { get; init; }
    public string? MirrorBaseUrl { get; init; }
}

public sealed record DownloadProgress(
    long DoneBytes,
    long TotalBytes,
    int DoneFiles,
    int TotalFiles,
    string? CurrentPath);

public sealed record DownloadFailure(string TargetPath, string Reason);

public sealed record DownloadReport(
    bool AllSucceeded,
    IReadOnlyList<DownloadFailure> Failed,
    long TotalBytes,
    long DoneBytes,
    int CompletedFiles,
    int TotalFiles);

public sealed class DownloadService
{
    private readonly HttpClient _httpClient;

    public DownloadService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherDefinitions.UserAgent);
        _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
    }

    public async Task<DownloadReport> DownloadAsync(
        IReadOnlyList<DownloadItem> items,
        DownloadOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DownloadOptions();
        var concurrency = Math.Clamp(options.MaxConcurrency, 1, 16);
        var uniqueItems = items.GroupBy(item => IoPath.GetFullPath(item.TargetPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
        var totalBytes = uniqueItems.Where(item => item.Size > 0).Sum(item => item.Size);
        var doneBytes = 0L;
        var completedFiles = 0;
        var failures = new List<DownloadFailure>();
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var tasks = uniqueItems.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await DownloadOneAsync(item, options, progress, totalBytes,
                    () => Interlocked.Read(ref doneBytes),
                    value => Interlocked.Exchange(ref doneBytes, value),
                    cancellationToken).ConfigureAwait(false);
                if (!result)
                {
                    lock (failures) failures.Add(new DownloadFailure(item.TargetPath, "下载内容校验失败。"));
                    return;
                }

                Interlocked.Increment(ref completedFiles);
                progress?.Report(new DownloadProgress(Interlocked.Read(ref doneBytes), totalBytes,
                    Volatile.Read(ref completedFiles), uniqueItems.Length, item.TargetPath));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lock (failures) failures.Add(new DownloadFailure(item.TargetPath, exception.Message));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return new DownloadReport(failures.Count == 0, failures, totalBytes,
            Interlocked.Read(ref doneBytes), completedFiles, uniqueItems.Length);
    }

    private async Task<bool> DownloadOneAsync(
        DownloadItem item,
        DownloadOptions options,
        IProgress<DownloadProgress>? progress,
        long totalBytes,
        Func<long> readDoneBytes,
        Action<long> writeDoneBytes,
        CancellationToken cancellationToken)
    {
        var target = IoPath.GetFullPath(item.TargetPath);
        Directory.CreateDirectory(IoPath.GetDirectoryName(target) ?? ".");
        if (File.Exists(target) && await MatchesAsync(target, item, cancellationToken).ConfigureAwait(false))
        {
            AddProgress(new FileInfo(target).Length, item, totalBytes, readDoneBytes, writeDoneBytes, progress);
            return true;
        }

        var part = target + ".part";
        for (var attempt = 0; attempt <= options.MaxRetry; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await DownloadAttemptAsync(item, target, part, options, totalBytes,
                    readDoneBytes, writeDoneBytes, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (attempt < options.MaxRetry
                                                         && (exception.StatusCode is null
                                                             || exception.StatusCode == HttpStatusCode.RequestTimeout
                                                             || (int)exception.StatusCode >= 500
                                                             || exception.StatusCode == (HttpStatusCode)429))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(4000, 500 * Math.Pow(2, attempt))), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return false;
    }

    private async Task<bool> DownloadAttemptAsync(
        DownloadItem item,
        string target,
        string part,
        DownloadOptions options,
        long totalBytes,
        Func<long> readDoneBytes,
        Action<long> writeDoneBytes,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partLength = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (item.Size > 0 && partLength > item.Size)
        {
            File.Delete(part);
            partLength = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ApplyMirror(item.Url, options.MirrorBaseUrl));
        if (partLength > 0) request.Headers.Range = new RangeHeaderValue(partLength, null);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if ((int)response.StatusCode >= 400 && response.StatusCode != HttpStatusCode.RequestTimeout
            && response.StatusCode != (HttpStatusCode)429)
        {
            throw new HttpRequestException($"服务器返回 HTTP {(int)response.StatusCode}。", null, response.StatusCode);
        }

        var append = partLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append) partLength = 0;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(part, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 1024 * 64, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[1024 * 1024];
        var current = partLength;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            current += read;
            var delta = current - partLength;
            AddProgress(delta, item, totalBytes, readDoneBytes, writeDoneBytes, progress);
            partLength = current;
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        await output.DisposeAsync().ConfigureAwait(false);
        await input.DisposeAsync().ConfigureAwait(false);
        if (item.Size > 0 && new FileInfo(part).Length != item.Size)
        {
            throw new HttpRequestException("下载文件大小不匹配。", null, HttpStatusCode.BadGateway);
        }

        if (!await MatchesAsync(part, item, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(part);
            throw new HttpRequestException("下载文件哈希不匹配。", null, HttpStatusCode.BadGateway);
        }

        File.Move(part, target, true);
        return true;
    }

    private static async Task<bool> MatchesAsync(string path, DownloadItem item, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (item.Size > 0 && info.Length != item.Size) return false;
        if (string.IsNullOrWhiteSpace(item.Sha1)) return true;
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(item.Sha1, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddProgress(long value, DownloadItem item, long totalBytes, Func<long> readDoneBytes,
        Action<long> writeDoneBytes, IProgress<DownloadProgress>? progress)
    {
        var done = readDoneBytes() + value;
        writeDoneBytes(done);
        progress?.Report(new DownloadProgress(done, totalBytes, 0, 0, item.TargetPath));
    }

    private static string ApplyMirror(string url, string? mirror)
    {
        if (string.IsNullOrWhiteSpace(mirror)) return url;
        var original = new Uri(url);
        var replacement = new Uri(mirror.TrimEnd('/') + "/");
        return new Uri(replacement, original.PathAndQuery.TrimStart('/')).ToString();
    }
}
