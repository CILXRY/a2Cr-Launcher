using a2Cr.Core.Download;
using a2Cr.Core.Files.Download;
using a2Cr.Core.Launcher;
using a2Cr.Core.Launcher.Definitions;

namespace a2Cr.Core.Files.Manifest;

public class ManifestList
{
    private readonly PathsDefinitions _pathsDefinitions = new();
    private string TargetDir => _pathsDefinitions.MetaDir;

    private string TargetUrl { get; } = LinksDefinitions.VersionsManifestList;

    public async Task GetManifestList()
    {
        var downloadTask = new DownloadItems();
        
        await DownloadItems.DownloadAsync(TargetUrl, Path.Join(TargetDir, "manifest.json"));
        // 下载 TargetUrl 到 TargetDir
        Console.WriteLine($"Downloading {TargetUrl} to {TargetDir}");
    }

}