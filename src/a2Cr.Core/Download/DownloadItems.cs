using Downloader;

namespace a2Cr.Core.Download;

public class DownloadItems
{
    private DownloadConfiguration config => new()
    {
        ChunkCount = 8,
    };

    public async Task DownloadAsync(string targetUrl, string targetPath)
    {
        var downloader = new Downloader.DownloadService(config);
        await downloader.DownloadFileTaskAsync(targetUrl, targetPath);
    }
}