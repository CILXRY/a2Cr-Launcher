using System.Security.Cryptography;
using a2Cr.Core.Download;
using a2Cr.Core.Errors;
using a2Cr.Core.Instances;
using a2Cr.Core.Launcher.Definitions;
using a2Cr.Core.Pool;
using a2Cr.Core.Storage;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Mods;

public sealed record ModInstallResult(
    string ProjectId,
    string VersionId,
    string VersionNumber,
    string Name,
    string FileName,
    bool Dependency);

public sealed record ModInstallReport(
    IReadOnlyList<ModInstallResult> Installed,
    IReadOnlyList<string> Skipped,
    ReconcileReport Reconcile);

public sealed record InstalledMod(string FileName, long Size, string? Sha1, FileOrigin Origin);

/// <summary>
/// 把一个 Modrinth 项目（含必需依赖）下载进池子，再对账到目标实例。
/// 池子路径是 <c>pool/mods/&lt;loader&gt;/&lt;mc_version&gt;/</c>，实例里只建硬链接。
/// </summary>
public sealed class ModInstaller
{
    private readonly PathsDefinitions _pathsDefinitions;
    private readonly InstanceManager _instances;
    private readonly ModrinthClient _client;
    private readonly DownloadService _downloader;
    private readonly PoolReconciler _reconciler;

    public ModInstaller(PathsDefinitions pathsDefinitions, InstanceManager instances, ModrinthClient client,
        DownloadService downloader, PoolReconciler? reconciler = null)
    {
        _pathsDefinitions = pathsDefinitions;
        _instances = instances;
        _client = client;
        _downloader = downloader;
        _reconciler = reconciler ?? new PoolReconciler(pathsDefinitions);
    }

    public async Task<ModInstallReport> InstallAsync(string folder, string project, string? versionId,
        bool withDependencies, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var metadata = await _instances.GetAsync(folder, cancellationToken).ConfigureAwait(false);
        var mcVersion = metadata.McVersion;
        var loader = string.IsNullOrWhiteSpace(metadata.LoaderType) ? "vanilla" : metadata.LoaderType;
        if (string.IsNullOrWhiteSpace(mcVersion))
        {
            throw new InstanceException("instance_broken", $"实例 {folder} 没有记录 mc_version。",
                "编辑 instance.json 补上 Minecraft 版本。");
        }

        if (loader == "vanilla")
        {
            throw new LauncherException("mod_needs_loader", "原版实例不能安装模组。",
                "请先创建 Fabric / Quilt 实例；Forge / NeoForge 将在后续里程碑提供。");
        }

        var main = await ResolveVersionAsync(project, mcVersion, loader, versionId, cancellationToken)
            .ConfigureAwait(false);
        var versions = new List<ModVersion> { main };
        var skipped = new List<string>();
        if (withDependencies)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(main.ProjectId)) seen.Add(main.ProjectId);
            await CollectRequiredAsync(main, mcVersion, loader, seen, versions, skipped, cancellationToken)
                .ConfigureAwait(false);
        }

        var poolDirectory = IoPath.Combine(_pathsDefinitions.ModsPoolDir, loader, mcVersion);
        Directory.CreateDirectory(poolDirectory);
        var downloads = new List<DownloadItem>();
        var results = new List<ModInstallResult>();
        var claimedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = version.PrimaryFile;
            if (file is null || string.IsNullOrWhiteSpace(file.Url))
            {
                skipped.Add($"{Display(version)} 没有可下载文件");
                continue;
            }

            var fileName = SafeFileName(file.FileName, file.Url);
            var target = IoPath.Combine(poolDirectory, fileName);
            if (claimedNames.TryGetValue(fileName, out var owner) && owner != version.ProjectId)
            {
                throw new StorageException($"两个模组使用了同一个文件名：{fileName}",
                    "先从池子里删掉冲突文件，再分别安装。");
            }

            claimedNames[fileName] = version.ProjectId;
            if (File.Exists(target) && !string.IsNullOrWhiteSpace(file.Sha1))
            {
                var existing = await HashAsync(target, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(existing, file.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    throw new StorageException($"池子里已有同名但内容不同的模组：{fileName}",
                        "先删除或重命名池子里的这个文件，再重新安装。");
                }
            }

            downloads.Add(new DownloadItem(file.Url, target, file.Sha1, file.Size));
            results.Add(new ModInstallResult(version.ProjectId, version.Id, version.VersionNumber,
                version.Name, fileName, !ReferenceEquals(version, main)));
        }

        if (downloads.Count == 0)
        {
            throw new LauncherException("mod_no_file", "没有解析到任何可下载的模组文件。",
                "检查项目是否支持当前实例的 Minecraft 版本和加载器。");
        }

        var report = await _downloader.DownloadAsync(downloads, progress: progress,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!report.AllSucceeded)
        {
            throw new DownloadException(report.Failed);
        }

        var reconcile = await _reconciler.ReconcileAsync(folder, metadata, cancellationToken)
            .ConfigureAwait(false);
        return new ModInstallReport(results, skipped, reconcile);
    }

    /// <summary>列出实例 <c>mods/</c> 里的文件；origin 来自清单，缺失时按用户文件处理。</summary>
    public async Task<IReadOnlyList<InstalledMod>> ListInstalledAsync(string folder,
        CancellationToken cancellationToken)
    {
        _ = await _instances.GetAsync(folder, cancellationToken).ConfigureAwait(false);
        var instanceDirectory = _instances.InstanceDirectory(folder);
        var modsDirectory = IoPath.Combine(instanceDirectory, "mods");
        if (!Directory.Exists(modsDirectory)) return [];

        var manifest = await InstanceManifest.LoadAsync(IoPath.Combine(instanceDirectory, "manifest.json"),
            cancellationToken).ConfigureAwait(false);
        var result = new List<InstalledMod>();
        foreach (var file in Directory.EnumerateFiles(modsDirectory, "*.jar", SearchOption.TopDirectoryOnly))
        {
            var relative = $"mods/{IoPath.GetFileName(file)}";
            var entry = manifest.Find(relative);
            result.Add(new InstalledMod(IoPath.GetFileName(file), new FileInfo(file).Length,
                entry?.Sha1, entry?.Origin ?? FileOrigin.User));
        }

        return result.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<ModVersion> ResolveVersionAsync(string project, string mcVersion, string loader,
        string? versionId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(versionId))
        {
            return await _client.GetVersionAsync(versionId, cancellationToken).ConfigureAwait(false);
        }

        var versions = await _client.GetVersionsAsync(project, mcVersion, loader, cancellationToken)
            .ConfigureAwait(false);
        var compatible = versions.Where(version =>
                version.GameVersions.Contains(mcVersion, StringComparer.OrdinalIgnoreCase)
                && version.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (compatible.Length == 0)
        {
            throw new LauncherException("mod_incompatible",
                $"{project} 没有适配 Minecraft {mcVersion} / {loader} 的版本。",
                "换一个项目，或先切换实例的版本 / 加载器。");
        }

        // 默认装正式版，避免把 beta / alpha 塞给用户。
        return compatible.FirstOrDefault(version => version.VersionType == "release") ?? compatible[0];
    }

    private async Task CollectRequiredAsync(ModVersion version, string mcVersion, string loader,
        HashSet<string> seen, List<ModVersion> result, List<string> skipped, CancellationToken cancellationToken)
    {
        foreach (var dependency in version.Dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dependency.DependencyType != "required") continue;
            var projectId = dependency.ProjectId;
            if (string.IsNullOrWhiteSpace(projectId) || !seen.Add(projectId)) continue;

            ModVersion? resolved;
            try
            {
                if (!string.IsNullOrWhiteSpace(dependency.VersionId))
                {
                    resolved = await _client.GetVersionAsync(dependency.VersionId, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    var versions = await _client.GetVersionsAsync(projectId, mcVersion, loader, cancellationToken)
                        .ConfigureAwait(false);
                    resolved = versions.Where(candidate =>
                            candidate.GameVersions.Contains(mcVersion, StringComparer.OrdinalIgnoreCase)
                            && candidate.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
                        .OrderBy(candidate => candidate.VersionType == "release" ? 0 : 1)
                        .FirstOrDefault();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (LauncherException exception)
            {
                // 单个依赖失败不应拖垮主模组安装；记录下来交给调用方提示。
                skipped.Add($"依赖 {projectId}：{exception.Message}");
                continue;
            }

            if (resolved is null)
            {
                skipped.Add($"依赖 {projectId} 没有适配 Minecraft {mcVersion} / {loader} 的版本");
                continue;
            }

            result.Add(resolved);
            await CollectRequiredAsync(resolved, mcVersion, loader, seen, result, skipped, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string Display(ModVersion version) =>
        string.IsNullOrWhiteSpace(version.Name) ? version.Id : version.Name;

    private static string SafeFileName(string fileName, string url)
    {
        var name = IoPath.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            var uri = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : null;
            name = uri is null ? string.Empty : IoPath.GetFileName(uri.AbsolutePath);
        }

        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            throw new LauncherException("mod_bad_file", "Modrinth 返回了无效的文件名。",
                "换一个项目版本后重试。");
        }

        return name;
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
