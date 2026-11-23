using System.Text.Json.Nodes;
using a2Cr.Core.Errors;
using a2Cr.Core.Storage;

namespace a2Cr.Core.Tests.Storage;

public sealed class InstanceManifestTests
{
    [Fact]
    public async Task Upsert_NormalizesBackslashesAndLeadingSlash()
    {
        using var root = new TestRoot();
        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(root.Root, "manifest.json"), CancellationToken.None);

        manifest.Upsert("\\mods\\Sodium.jar", "abc", FileOrigin.Pool);

        var entry = Assert.Single(manifest.Entries);
        Assert.Equal("mods/Sodium.jar", entry.Path);
        Assert.Equal(FileOrigin.Pool, entry.Origin);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingEntryInsteadOfDuplicating()
    {
        using var root = new TestRoot();
        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(root.Root, "manifest.json"), CancellationToken.None);

        manifest.Upsert("mods/a.jar", "old", FileOrigin.Pool, "old");
        manifest.Upsert("MODS\\A.JAR", "new", FileOrigin.User, "new");

        var entry = Assert.Single(manifest.Entries);
        Assert.Equal("new", entry.Sha1);
        Assert.Equal(FileOrigin.User, entry.Origin);
        Assert.Equal("new", entry.Sha1OnInstall);
    }

    [Fact]
    public async Task Load_PreservesUnknownRootFieldsWhenSaving()
    {
        using var root = new TestRoot();
        var path = System.IO.Path.Combine(root.Root, "manifest.json");
        await File.WriteAllTextAsync(path,
            "{\"format\":1,\"files\":[],\"future\":{\"value\":42}}");

        var manifest = await InstanceManifest.LoadAsync(path, CancellationToken.None);
        manifest.Upsert("mods/a.jar", "abc", FileOrigin.Pack);
        await manifest.SaveAsync(path, CancellationToken.None);

        var saved = await AtomicJson.ReadObjectAsync(path, CancellationToken.None);
        Assert.Equal(42, saved!["future"]!["value"]!.GetValue<int>());
    }

    [Fact]
    public async Task Load_RejectsHigherFormat()
    {
        using var root = new TestRoot();
        var path = System.IO.Path.Combine(root.Root, "manifest.json");
        await File.WriteAllTextAsync(path, "{\"format\":2,\"files\":[]}");

        var exception = await Assert.ThrowsAsync<InstanceException>(
            () => InstanceManifest.LoadAsync(path, CancellationToken.None));

        Assert.Equal("instance_broken", exception.Code);
    }

    [Fact]
    public async Task Remove_DeletesEntry()
    {
        using var root = new TestRoot();
        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(root.Root, "manifest.json"), CancellationToken.None);
        manifest.Upsert("mods/a.jar", "abc", FileOrigin.Pool);

        Assert.True(manifest.Remove("mods\\a.jar"));
        Assert.Empty(manifest.Entries);
        Assert.False(manifest.Remove("mods/a.jar"));
    }
}
