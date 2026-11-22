using System.Security.Cryptography;
using a2Cr.Core.Instances;
using a2Cr.Core.Path;
using a2Cr.Core.Storage;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Pool;

public sealed record ReconcileReport(int Linked, int Copied, int Removed, int UserFiles)
{
    public int TotalChanged => Linked + Copied + Removed;
}

public sealed class PoolReconciler
{
    private readonly LauncherPaths _paths;

    public PoolReconciler(LauncherPaths paths)
    {
        _paths = paths;
    }

    public async Task<ReconcileReport> ReconcileAsync(string folder, InstanceMetadata metadata,
        CancellationToken cancellationToken)
    {
        var instanceDirectory = IoPath.Combine(_paths.InstancesDir, folder);
        Directory.CreateDirectory(instanceDirectory);
        foreach (var category in ManagedCategories)
        {
            Directory.CreateDirectory(IoPath.Combine(instanceDirectory, category));
        }

        var manifestPath = IoPath.Combine(instanceDirectory, "manifest.json");
        var manifest = await InstanceManifest.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var sources = BuildSources(metadata);
        var linked = 0;
        var copied = 0;
        var removed = 0;
        var userFiles = 0;

        foreach (var entry in manifest.Entries.Where(entry => entry.Origin == FileOrigin.Pool
                     && IsManaged(entry.Path)).ToArray())
        {
            if (sources.ContainsKey(entry.Path)) continue;
            var target = IoPath.Combine(instanceDirectory, entry.Path.Replace('/', IoPath.DirectorySeparatorChar));
            if (!File.Exists(target))
            {
                manifest.Remove(entry.Path);
                continue;
            }

            var hash = await HashAsync(target, cancellationToken).ConfigureAwait(false);
            if (entry.Sha1 is null || string.Equals(hash, entry.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                manifest.Remove(entry.Path);
                removed++;
            }
            else
            {
                manifest.Upsert(entry.Path, hash, FileOrigin.User, hash);
                userFiles++;
            }
        }

        foreach (var (relative, source) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = IoPath.Combine(instanceDirectory, relative.Replace('/', IoPath.DirectorySeparatorChar));
            var sourceHash = await HashAsync(source, cancellationToken).ConfigureAwait(false);
            if (File.Exists(target))
            {
                var existing = manifest.Find(relative);
                if (existing?.Origin == FileOrigin.Pool)
                {
                    var targetHash = await HashAsync(target, cancellationToken).ConfigureAwait(false);
                    if (string.Equals(targetHash, sourceHash, StringComparison.OrdinalIgnoreCase))
                    {
                        manifest.Upsert(relative, targetHash, FileOrigin.Pool, targetHash);
                        continue;
                    }
                }

                if (existing?.Origin != FileOrigin.User)
                {
                    var targetHash = await HashAsync(target, cancellationToken).ConfigureAwait(false);
                    manifest.Upsert(relative, targetHash, FileOrigin.User, targetHash);
                    userFiles++;
                }

                continue;
            }

            Directory.CreateDirectory(IoPath.GetDirectoryName(target)!);
            if (HardLink.TryCreate(source, target))
            {
                linked++;
            }
            else
            {
                await AtomicJson.CopyAsync(source, target, cancellationToken).ConfigureAwait(false);
                copied++;
            }

            manifest.Upsert(relative, sourceHash, FileOrigin.Pool, sourceHash);
        }

        foreach (var relative in EnumerateManagedFiles(instanceDirectory))
        {
            if (manifest.Find(relative) is not null) continue;
            var target = IoPath.Combine(instanceDirectory, relative.Replace('/', IoPath.DirectorySeparatorChar));
            var hash = await HashAsync(target, cancellationToken).ConfigureAwait(false);
            manifest.Upsert(relative, hash, FileOrigin.User, hash);
            userFiles++;
        }

        await manifest.SaveAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        return new ReconcileReport(linked, copied, removed, userFiles);
    }

    public static readonly string[] ManagedCategories = ["mods", "resourcepacks", "shaderpacks"];

    private Dictionary<string, string> BuildSources(InstanceMetadata metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var loader = string.IsNullOrWhiteSpace(metadata.LoaderType) ? "vanilla" : metadata.LoaderType;
        if (!string.IsNullOrWhiteSpace(metadata.McVersion))
        {
            AddCategory(result, "mods", IoPath.Combine(_paths.ModsPoolDir, loader, metadata.McVersion));
        }

        AddCategory(result, "resourcepacks", _paths.ResourcePacksPoolDir);
        AddCategory(result, "shaderpacks", _paths.ShaderPacksPoolDir);
        return result;
    }

    private static void AddCategory(IDictionary<string, string> result, string category, string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var name = IoPath.GetFileName(file);
            if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            var relative = IoPath.GetRelativePath(root, file).Replace('\\', '/');
            result[$"{category}/{relative}"] = file;
        }
    }

    private static IEnumerable<string> EnumerateManagedFiles(string instanceDirectory)
    {
        foreach (var category in ManagedCategories)
        {
            var root = IoPath.Combine(instanceDirectory, category);
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var name = IoPath.GetFileName(file);
                if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                yield return IoPath.GetRelativePath(instanceDirectory, file).Replace('\\', '/');
            }
        }
    }

    private static bool IsManaged(string path) => ManagedCategories.Any(category =>
        path.StartsWith(category + "/", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
