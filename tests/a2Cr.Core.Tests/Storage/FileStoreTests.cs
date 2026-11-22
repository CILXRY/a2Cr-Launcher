using System.Security.Cryptography;
using a2Cr.Core.Download;
using a2Cr.Core.Errors;
using a2Cr.Core.Storage;

namespace a2Cr.Core.Tests.Storage;

public sealed class FileStoreTests
{
    [Fact]
    public async Task AcquireAsync_CopiesLocalFileAndRecordsManifest()
    {
        using var root = new TestRoot();
        var (instance, store) = await CreateStoreAsync(root);
        var source = System.IO.Path.Combine(root.Root, "source.jar");
        await File.WriteAllTextAsync(source, "mod-content");
        var target = System.IO.Path.Combine(instance, "mods", "a.jar");

        var result = await store.AcquireAsync(target, ContentSpec.Local(source), FileOrigin.Pack,
            null, CancellationToken.None);

        Assert.Equal(AcquireStatus.Success, result.Status);
        Assert.Equal("mod-content", await File.ReadAllTextAsync(target));
        Assert.Equal(Sha1("mod-content"), result.Sha1);
        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(instance, "manifest.json"), CancellationToken.None);
        var entry = Assert.Single(manifest.Entries);
        Assert.Equal("mods/a.jar", entry.Path);
        Assert.Equal(FileOrigin.Pack, entry.Origin);
        Assert.Equal(result.Sha1, entry.Sha1OnInstall);
    }

    [Fact]
    public async Task AcquireAsync_SkipsWhenContentMatches()
    {
        using var root = new TestRoot();
        var (instance, store) = await CreateStoreAsync(root);
        var source = System.IO.Path.Combine(root.Root, "source.jar");
        await File.WriteAllTextAsync(source, "same-content");
        var target = System.IO.Path.Combine(instance, "mods", "a.jar");

        await store.AcquireAsync(target, ContentSpec.Local(source), FileOrigin.Pack, null, CancellationToken.None);
        var second = await store.AcquireAsync(target, ContentSpec.Local(source), FileOrigin.Pack, null,
            CancellationToken.None);

        Assert.Equal(AcquireStatus.Skipped, second.Status);
    }

    [Fact]
    public async Task AcquireAsync_ReplacesHardLinkWithoutWritingThroughToPool()
    {
        using var root = new TestRoot();
        var (instance, store) = await CreateStoreAsync(root);
        var poolFile = System.IO.Path.Combine(root.Root, "pool", "sodium.jar");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(poolFile)!);
        await File.WriteAllTextAsync(poolFile, "pool-content");
        var target = System.IO.Path.Combine(instance, "mods", "sodium.jar");
        Assert.True(HardLink.TryCreate(poolFile, target));

        var replacement = System.IO.Path.Combine(root.Root, "replacement.jar");
        await File.WriteAllTextAsync(replacement, "new-local-content");
        await store.AcquireAsync(target, ContentSpec.Local(replacement), FileOrigin.Pack, null,
            CancellationToken.None);

        Assert.Equal("pool-content", await File.ReadAllTextAsync(poolFile));
        Assert.Equal("new-local-content", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task AcquireAsync_RejectsPathOutsideInstance()
    {
        using var root = new TestRoot();
        var (instance, store) = await CreateStoreAsync(root);
        var source = System.IO.Path.Combine(root.Root, "source.jar");
        await File.WriteAllTextAsync(source, "content");
        var outside = System.IO.Path.Combine(instance, "..", "outside.jar");

        var exception = await Assert.ThrowsAsync<StorageException>(() =>
            store.AcquireAsync(outside, ContentSpec.Local(source), FileOrigin.Pack, null,
                CancellationToken.None));

        Assert.Equal("storage_failed", exception.Code);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task ReleaseAsync_DeletesFileAndManifestEntry()
    {
        using var root = new TestRoot();
        var (instance, store) = await CreateStoreAsync(root);
        var source = System.IO.Path.Combine(root.Root, "source.jar");
        await File.WriteAllTextAsync(source, "content");
        var target = System.IO.Path.Combine(instance, "mods", "a.jar");
        await store.AcquireAsync(target, ContentSpec.Local(source), FileOrigin.Pack, null, CancellationToken.None);

        await store.ReleaseAsync(target, CancellationToken.None);

        Assert.False(File.Exists(target));
        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(instance, "manifest.json"), CancellationToken.None);
        Assert.Empty(manifest.Entries);
    }

    private static async Task<(string Instance, InstanceFileStore Store)> CreateStoreAsync(TestRoot root)
    {
        var instance = System.IO.Path.Combine(root.Root, "instances", "demo");
        Directory.CreateDirectory(instance);
        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(instance, "manifest.json"), CancellationToken.None);
        return (instance, new InstanceFileStore(instance, manifest, new DownloadService()));
    }

    private static string Sha1(string content) =>
        Convert.ToHexString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
