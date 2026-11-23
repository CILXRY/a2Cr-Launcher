using a2Cr.Core.Launcher.Definitions;
using Downloader;

namespace a2Cr.Core.Files.Download;

public abstract class DownloaderConfig
{
    public static DownloadConfiguration Config => new()
    {
        ChunkCount = 8,
        RequestConfiguration =
        {
            UserAgent = LauncherDefinitions.UserAgent
        }
    };
}