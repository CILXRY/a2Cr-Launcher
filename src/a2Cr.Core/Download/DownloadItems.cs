using Downloader;

namespace a2Cr.Core.Download;

public class DownloadItems
{
    public static async Task DownloadAsync(string targetUrl, string targetPath)
    {
        var downloader = new Downloader.DownloadService(DownloaderConfig.Config);
        await downloader.DownloadFileTaskAsync(targetUrl, targetPath);
    }
}