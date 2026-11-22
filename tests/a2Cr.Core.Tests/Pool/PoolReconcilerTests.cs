using a2Cr.Core.Instances;
using a2Cr.Core.Path;
using a2Cr.Core.Pool;
using a2Cr.Core.Settings;
using a2Cr.Core.Storage;

namespace a2Cr.Core.Tests.Pool;

public sealed class PoolReconcilerTests
{
    [Fact]
    public async Task Reconcile_RemovesStalePoolLinksButKeepsUserFiles()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var manager = await CreateManagerAsync(paths);
        var poolMod = await WritePoolModAsync(paths, "sodium.jar", "sodium");
        var created = await manager.CreateAsync("Pack", "1.21.1", "fabric-loader-0.16.9-1.21.1",
            "fabric", "0.16.9", cancellationToken: CancellationToken.None);
        var directory = manager.InstanceDirectory(created.Metadata.Folder);
        var manual = System.IO.Path.Combine(directory, "mods", "manual.jar");
        await File.WriteAllTextAsync(manual, "user");
        File.Delete(poolMod);

        var report = await new PoolReconciler(paths).ReconcileAsync(created.Metadata.Folder,
            await manager.GetAsync(created.Metadata.Folder, CancellationToken.None), CancellationToken.None);

        Assert.Equal(1, report.Removed);
        Assert.False(File.Exists(System.IO.Path.Combine(directory, "mods", "sodium.jar")));
        Assert.True(File.Exists(manual));
        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(directory, "manifest.json"), CancellationToken.None);
        Assert.Equal(FileOrigin.User, Assert.Single(manifest.Entries).Origin);
    }

    [Fact]
    public async Task Reconcile_KeepsUserModifiedLinkAsUserFile()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var manager = await CreateManagerAsync(paths);
        var poolMod = await WritePoolModAsync(paths, "sodium.jar", "pool");
        var created = await manager.CreateAsync("Pack", "1.21.1", "fabric-loader-0.16.9-1.21.1",
            "fabric", "0.16.9", cancellationToken: CancellationToken.None);
        var directory = manager.InstanceDirectory(created.Metadata.Folder);
        var target = System.IO.Path.Combine(directory, "mods", "sodium.jar");
        var temporary = target + ".edit";
        await File.WriteAllTextAsync(temporary, "user-edit");
        File.Move(temporary, target, true);
        await File.WriteAllTextAsync(poolMod, "pool-updated");

        var report = await new PoolReconciler(paths).ReconcileAsync(created.Metadata.Folder,
            await manager.GetAsync(created.Metadata.Folder, CancellationToken.None), CancellationToken.None);

        Assert.Equal(1, report.UserFiles);
        Assert.Equal("user-edit", await File.ReadAllTextAsync(target));
        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(directory, "manifest.json"), CancellationToken.None);
        Assert.Equal(FileOrigin.User, Assert.Single(manifest.Entries).Origin);
    }

    [Fact]
    public async Task Reconcile_RecordsUnlistedManualFileAsUser()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var manager = await CreateManagerAsync(paths);
        var created = await manager.CreateAsync("Pack", "1.21.1", "1.21.1", cancellationToken: CancellationToken.None);
        var directory = manager.InstanceDirectory(created.Metadata.Folder);
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "mods", "manual.jar"), "user");

        await new PoolReconciler(paths).ReconcileAsync(created.Metadata.Folder,
            await manager.GetAsync(created.Metadata.Folder, CancellationToken.None), CancellationToken.None);

        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(directory, "manifest.json"), CancellationToken.None);
        var entry = Assert.Single(manifest.Entries);
        Assert.Equal("mods/manual.jar", entry.Path);
        Assert.Equal(FileOrigin.User, entry.Origin);
    }

    private static async Task<InstanceManager> CreateManagerAsync(LauncherPaths paths)
    {
        var settings = await LauncherSettings.LoadAsync(paths, CancellationToken.None);
        return new InstanceManager(paths, settings);
    }

    private static async Task<string> WritePoolModAsync(LauncherPaths paths, string name, string content)
    {
        var directory = System.IO.Path.Combine(paths.ModsPoolDir, "fabric", "1.21.1");
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
