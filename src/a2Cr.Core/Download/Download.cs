using Downloader;
using System.Reflection;

namespace a2Cr.Core.Download;

public abstract class Download
{
    public sealed record DownloadResult(bool Ok, string? ErrorMessage);

    public static async Task<DownloadResult> DownloadFile(string url, string path)
    {
        var downloadOpt = new DownloadConfiguration
        {
            // split into 8 parts
            ChunkCount = 4,
            // download chunks as parallel (fast)        
            ParallelDownload = true,
            // cap concurrent connections to be a good citizen
            ParallelCount = 4,
            // retry transient network errors before giving up
            MaxTryAgainOnFailure = 5,
            // survive crashes/network drops with zero extra code
            EnableAutoResumeDownload = true,
            // cap RAM used for buffering (50MB)
            MaximumMemoryBufferBytes = 50 * 1024 * 1024,
            // fail early if there isn't enough disk space
            CheckDiskSizeBeforeDownload = true,
            // 0 = unlimited; set a value to limit speed B/s
            MaximumBytesPerSecond = 0,

            RequestConfiguration =
            {
                // User-Agent: a2cr-launcher/<版本号>
                UserAgent = $"a2cr-launcher/{Assembly.GetExecutingAssembly().GetName().Version}"
            }
        };
        string? errorMessage = null;

        var downloader = new DownloadService(downloadOpt);
        downloader.DownloadFileCompleted += (_, e) => { errorMessage = e.Error?.Message; };
        await downloader.DownloadFileTaskAsync(url, path);
        return new DownloadResult(downloader.Package.Status == DownloadStatus.Completed, errorMessage);
    }
}