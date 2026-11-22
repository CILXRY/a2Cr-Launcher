using System.Security.Cryptography;
using System.Text.Json.Nodes;
using a2Cr.Core.Download;
using a2Cr.Core.Errors;
using a2Cr.Core.Path;
using a2Cr.Core.Pool;
using a2Cr.Core.Settings;
using a2Cr.Core.Storage;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Instances;

public sealed record InstanceSummary(string Folder, string Name, string McVersion, string VersionId,
    string LoaderType, string LoaderVersion, string? LastPlayed, bool Broken, string? Error);

public sealed record CreateInstanceResult(InstanceMetadata Metadata, ReconcileReport Reconcile);

public sealed class InstanceManager
{
    private readonly LauncherPaths _paths;
    private readonly LauncherSettings _settings;
    private readonly PoolReconciler _reconciler;

    public InstanceManager(LauncherPaths paths, LauncherSettings settings, PoolReconciler? reconciler = null)
    {
        _paths = paths;
        _settings = settings;
        _reconciler = reconciler ?? new PoolReconciler(paths);
    }

    public async Task<IReadOnlyList<InstanceSummary>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.InstancesDir)) return [];
        var result = new List<InstanceSummary>();
        foreach (var directory in Directory.EnumerateDirectories(_paths.InstancesDir)
                     .OrderBy(path => IoPath.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            var folder = IoPath.GetFileName(directory);
            if (folder.StartsWith(".tmp-", StringComparison.OrdinalIgnoreCase)) continue;
            var load = await LoadAsync(folder, cancellationToken).ConfigureAwait(false);
            if (load.Metadata is { } metadata)
            {
                result.Add(new InstanceSummary(folder, metadata.Name, metadata.McVersion, metadata.VersionId,
                    metadata.LoaderType, metadata.LoaderVersion, metadata.LastPlayed, false, null));
            }
            else if (load.Status == InstanceLoadStatus.Broken)
            {
                result.Add(new InstanceSummary(folder, folder, string.Empty, string.Empty, string.Empty,
                    string.Empty, null, true, load.Error ?? "instance.json 损坏"));
            }

            // 没有 instance.json 的目录不是实例（例如 `launch --version` 直接用的游戏目录），列表里跳过。
        }

        return result;
    }

    public async Task<InstanceMetadata> GetAsync(string folder, CancellationToken cancellationToken)
    {
        var load = await LoadAsync(folder, cancellationToken).ConfigureAwait(false);
        return load.Status switch
        {
            InstanceLoadStatus.Ok => load.Metadata!,
            InstanceLoadStatus.Broken => throw InstanceException.Broken(MetaPath(folder)),
            _ => throw InstanceException.Missing(folder)
        };
    }

    public async Task<CreateInstanceResult> CreateAsync(string name, string mcVersion, string versionId,
        string loaderType = "vanilla", string loaderVersion = "", JsonObject? source = null,
        string? template = null, CancellationToken cancellationToken = default)
    {
        var folder = InstanceNaming.CreateFolderName(name, _settings.InstanceIdStyle,
            candidate => Directory.Exists(IoPath.Combine(_paths.InstancesDir, candidate)));
        var directory = InstanceDirectory(folder);
        Directory.CreateDirectory(directory);
        try
        {
            var metadata = InstanceMetadata.Create(folder, name, mcVersion, versionId, loaderType,
                loaderVersion, source, template);
            await metadata.SaveAsync(MetaPath(folder), cancellationToken).ConfigureAwait(false);
            CreatePrivateDirectories(directory);
            var manifest = await InstanceManifest.LoadAsync(ManifestPath(folder), cancellationToken)
                .ConfigureAwait(false);
            await manifest.SaveAsync(ManifestPath(folder), cancellationToken).ConfigureAwait(false);
            var report = await _reconciler.ReconcileAsync(folder, metadata, cancellationToken)
                .ConfigureAwait(false);
            return new CreateInstanceResult(metadata, report);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    public async Task<CreateInstanceResult> CloneAsync(string sourceFolder, string newName, bool copy,
        CancellationToken cancellationToken)
    {
        var source = await GetAsync(sourceFolder, cancellationToken).ConfigureAwait(false);
        var sourceDirectory = InstanceDirectory(sourceFolder);
        var finalFolder = InstanceNaming.CreateFolderName(newName, _settings.InstanceIdStyle,
            candidate => Directory.Exists(IoPath.Combine(_paths.InstancesDir, candidate)));
        var temporaryFolder = ".tmp-" + Guid.NewGuid().ToString("N");
        var temporaryDirectory = InstanceDirectory(temporaryFolder);
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var metadata = InstanceMetadata.Create(finalFolder, newName, source.McVersion, source.VersionId,
                source.LoaderType, source.LoaderVersion, source.Source, source.Template, sourceFolder);
            await metadata.SaveAsync(IoPath.Combine(temporaryDirectory, "instance.json"), cancellationToken)
                .ConfigureAwait(false);
            CreatePrivateDirectories(temporaryDirectory);
            var manifest = await InstanceManifest.LoadAsync(
                IoPath.Combine(temporaryDirectory, "manifest.json"), cancellationToken).ConfigureAwait(false);
            await CopyPrivateFilesAsync(sourceDirectory, temporaryDirectory, manifest, cancellationToken)
                .ConfigureAwait(false);

            ReconcileReport report;
            if (copy)
            {
                foreach (var category in PoolReconciler.ManagedCategories)
                {
                    await CopyManagedFilesAsync(IoPath.Combine(sourceDirectory, category),
                        IoPath.Combine(temporaryDirectory, category), manifest, cancellationToken)
                        .ConfigureAwait(false);
                }

                await manifest.SaveAsync(IoPath.Combine(temporaryDirectory, "manifest.json"), cancellationToken)
                    .ConfigureAwait(false);
                report = new ReconcileReport(0, 0, 0, 0);
            }
            else
            {
                await manifest.SaveAsync(IoPath.Combine(temporaryDirectory, "manifest.json"), cancellationToken)
                    .ConfigureAwait(false);
                report = await _reconciler.ReconcileAsync(temporaryFolder, metadata, cancellationToken)
                    .ConfigureAwait(false);
            }

            var finalDirectory = InstanceDirectory(finalFolder);
            Directory.Move(temporaryDirectory, finalDirectory);
            return new CreateInstanceResult(metadata, report);
        }
        catch
        {
            TryDeleteDirectory(temporaryDirectory);
            throw;
        }
    }

    public async Task<string> RemoveAsync(string folder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = InstanceDirectory(folder);
        if (!Directory.Exists(directory)) throw InstanceException.Missing(folder);
        Directory.CreateDirectory(_paths.TrashesDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var target = IoPath.Combine(_paths.TrashesDir, $"{folder}-{stamp}");
        var index = 2;
        while (Directory.Exists(target)) target = IoPath.Combine(_paths.TrashesDir, $"{folder}-{stamp}-{index++}");
        Directory.Move(directory, target);
        await Task.CompletedTask.ConfigureAwait(false);
        return target;
    }

    public async Task MarkPlayedAsync(string folder, CancellationToken cancellationToken)
    {
        var metadata = await GetAsync(folder, cancellationToken).ConfigureAwait(false);
        await metadata.MarkPlayedAsync(MetaPath(folder), cancellationToken).ConfigureAwait(false);
    }

    public string InstanceDirectory(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || folder is "." or ".."
            || folder.IndexOfAny([IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InstanceException("instance_missing", $"非法实例目录：{folder}", "只允许 instances 下的一级目录。");
        }

        var directory = IoPath.GetFullPath(IoPath.Combine(_paths.InstancesDir, folder));
        var root = IoPath.GetFullPath(_paths.InstancesDir).TrimEnd(IoPath.DirectorySeparatorChar)
                   + IoPath.DirectorySeparatorChar;
        if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InstanceException("instance_missing", $"实例目录越界：{folder}", "只允许 instances 下的一级目录。");
        }

        return directory;
    }

    public string MetaPath(string folder) => IoPath.Combine(InstanceDirectory(folder), "instance.json");

    public string ManifestPath(string folder) => IoPath.Combine(InstanceDirectory(folder), "manifest.json");

    private async Task<InstanceLoadResult> LoadAsync(string folder, CancellationToken cancellationToken)
    {
        var directory = InstanceDirectory(folder);
        if (!Directory.Exists(directory))
        {
            return new InstanceLoadResult(InstanceLoadStatus.Missing, null, null);
        }

        return await InstanceMetadata.LoadAsync(folder, MetaPath(folder), cancellationToken)
            .ConfigureAwait(false);
    }

    private static void CreatePrivateDirectories(string directory)
    {
        foreach (var name in new[] { "mods", "resourcepacks", "shaderpacks", "config", "logs", "crash-reports", "saves" })
        {
            Directory.CreateDirectory(IoPath.Combine(directory, name));
        }
    }

    private static async Task CopyPrivateFilesAsync(string source, string target, InstanceManifest manifest,
        CancellationToken cancellationToken)
    {
        await CopyDirectoryAsync(IoPath.Combine(source, "config"), IoPath.Combine(target, "config"),
            manifest, "config", cancellationToken).ConfigureAwait(false);
        foreach (var name in new[] { "options.txt", "servers.dat" })
        {
            var file = IoPath.Combine(source, name);
            if (File.Exists(file))
            {
                await AtomicJson.CopyAsync(file, IoPath.Combine(target, name), cancellationToken).ConfigureAwait(false);
                manifest.Upsert(name, await HashAsync(IoPath.Combine(target, name), cancellationToken)
                    .ConfigureAwait(false), FileOrigin.User);
            }
        }

        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*.dat", SearchOption.TopDirectoryOnly))
        {
            var name = IoPath.GetFileName(file);
            await AtomicJson.CopyAsync(file, IoPath.Combine(target, name), cancellationToken).ConfigureAwait(false);
            manifest.Upsert(name, await HashAsync(IoPath.Combine(target, name), cancellationToken)
                .ConfigureAwait(false), FileOrigin.User);
        }
    }

    private static async Task CopyManagedFilesAsync(string source, string target, InstanceManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = IoPath.GetRelativePath(source, file);
            var destination = IoPath.Combine(target, relative);
            await AtomicJson.CopyAsync(file, destination, cancellationToken).ConfigureAwait(false);
            var category = IoPath.GetFileName(target);
            manifest.Upsert($"{category}/{relative.Replace('\\', '/')}",
                await HashAsync(destination, cancellationToken).ConfigureAwait(false), FileOrigin.User);
        }
    }

    private static async Task CopyDirectoryAsync(string source, string target, InstanceManifest manifest,
        string prefix, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = IoPath.GetRelativePath(source, file);
            var destination = IoPath.Combine(target, relative);
            await AtomicJson.CopyAsync(file, destination, cancellationToken).ConfigureAwait(false);
            manifest.Upsert($"{prefix}/{relative.Replace('\\', '/')}",
                await HashAsync(destination, cancellationToken).ConfigureAwait(false), FileOrigin.User);
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
