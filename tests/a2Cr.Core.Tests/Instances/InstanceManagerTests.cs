using a2Cr.Core.Errors;
using a2Cr.Core.Instances;
using a2Cr.Core.Path;
using a2Cr.Core.Settings;
using a2Cr.Core.Storage;

namespace a2Cr.Core.Tests.Instances;

public sealed class InstanceManagerTests
{
    [Fact]
    public async Task CreateAsync_WritesMetadataAndLinksPoolFiles()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var manager = await CreateManagerAsync(paths);
        await WritePoolModAsync(paths, "fabric", "1.21.1", "sodium.jar", "sodium");

        var result = await manager.CreateAsync("Mechanical Pack", "1.21.1",
            "fabric-loader-0.16.9-1.21.1", "fabric", "0.16.9", cancellationToken: CancellationToken.None);

        Assert.Equal("mechanical-pack", result.Metadata.Folder);
        Assert.True(File.Exists(manager.MetaPath("mechanical-pack")));
        Assert.True(File.Exists(System.IO.Path.Combine(manager.InstanceDirectory("mechanical-pack"),
            "mods", "sodium.jar")));
        Assert.Equal(1, result.Reconcile.Linked + result.Reconcile.Copied);
        var loaded = await manager.GetAsync("mechanical-pack", CancellationToken.None);
        Assert.Equal("fabric", loaded.LoaderType);
        Assert.Equal("1.21.1", loaded.McVersion);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameGetsSuffix()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var manager = await CreateManagerAsync(paths);

        var first = await manager.CreateAsync("Pack", "1.21.1", "1.21.1", cancellationToken: CancellationToken.None);
        var second = await manager.CreateAsync("Pack", "1.21.1", "1.21.1", cancellationToken: CancellationToken.None);

        Assert.Equal("pack", first.Metadata.Folder);
        Assert.Equal("pack-2", second.Metadata.Folder);
    }

    [Fact]
    public async Task CloneAsync_LinksPoolFilesAndSkipsUserFiles()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var manager = await CreateManagerAsync(paths);
        await WritePoolModAsync(paths, "fabric", "1.21.1", "sodium.jar", "sodium");
        var source = await manager.CreateAsync("Source", "1.21.1", "fabric-loader-0.16.9-1.21.1",
            "fabric", "0.16.9", cancellationToken: CancellationToken.None);
        var userMod = System.IO.Path.Combine(manager.InstanceDirectory(source.Metadata.Folder), "mods", "manual.jar");
        await File.WriteAllTextAsync(userMod, "user");

        var clone = await manager.CloneAsync(source.Metadata.Folder, "Clone", false, CancellationToken.None);

        var cloneDirectory = manager.InstanceDirectory(clone.Metadata.Folder);
        Assert.True(File.Exists(System.IO.Path.Combine(cloneDirectory, "mods", "sodium.jar")));
        Assert.False(File.Exists(System.IO.Path.Combine(cloneDirectory, "mods", "manual.jar")));
        Assert.Equal(source.Metadata.Folder, clone.Metadata.ClonedFrom);
        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(cloneDirectory, "manifest.json"), CancellationToken.None);
        Assert.Equal(FileOrigin.Pool, Assert.Single(manifest.Entries).Origin);
        Assert.Empty(Directory.EnumerateDirectories(paths.InstancesDir, ".tmp-*"));
    }

    [Fact]
    public async Task RemoveAsync_MovesInstanceToTrash()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var manager = await CreateManagerAsync(paths);
        var created = await manager.CreateAsync("Pack", "1.21.1", "1.21.1", cancellationToken: CancellationToken.None);

        var trash = await manager.RemoveAsync(created.Metadata.Folder, CancellationToken.None);

        Assert.False(Directory.Exists(manager.InstanceDirectory(created.Metadata.Folder)));
        Assert.True(Directory.Exists(trash));
        Assert.StartsWith(System.IO.Path.Combine(paths.TrashesDir, "pack-"), trash);
    }

    [Fact]
    public async Task GetAsync_MissingInstanceThrowsStableCode()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var manager = await CreateManagerAsync(paths);

        var exception = await Assert.ThrowsAsync<InstanceException>(
            () => manager.GetAsync("nope", CancellationToken.None));

        Assert.Equal("instance_missing", exception.Code);
    }

    [Fact]
    public async Task ListAsync_ReportsBrokenMetadataWithoutCrashing()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var manager = await CreateManagerAsync(paths);
        var folder = System.IO.Path.Combine(paths.InstancesDir, "broken");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(System.IO.Path.Combine(folder, "instance.json"), "{broken");

        var list = await manager.ListAsync(CancellationToken.None);

        var item = Assert.Single(list);
        Assert.True(item.Broken);
        Assert.Equal("broken", item.Folder);
    }

    private static async Task<InstanceManager> CreateManagerAsync(LauncherPaths paths)
    {
        var settings = await LauncherSettings.LoadAsync(paths, CancellationToken.None);
        return new InstanceManager(paths, settings);
    }

    private static async Task WritePoolModAsync(LauncherPaths paths, string loader, string mcVersion,
        string name, string content)
    {
        var directory = System.IO.Path.Combine(paths.ModsPoolDir, loader, mcVersion);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(System.IO.Path.Combine(directory, name), content);
    }
}
