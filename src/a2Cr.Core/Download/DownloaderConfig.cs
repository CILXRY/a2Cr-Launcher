using Downloader;

namespace a2Cr.Core.Download;

public abstract class DownloaderConfig
{
    public static DownloadConfiguration Config => new()
    {
        ChunkCount = 8,
        RequestConfiguration =
        {
            UserAgent = Launcher.LauncherInfo.UserAgent
        }
    };
}