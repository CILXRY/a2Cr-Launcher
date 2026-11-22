using a2Cr.Core.Download;

namespace a2Cr.Core.Meta;

/// <summary>安装一个版本的运行文件；加载器安装器用它保证父版本已就绪。</summary>
public interface IVersionInstaller
{
    Task InstallAsync(string versionId, bool refresh, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken);
}
