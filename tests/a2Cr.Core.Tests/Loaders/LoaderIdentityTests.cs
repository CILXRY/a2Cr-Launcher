using System.Text.Json.Nodes;
using a2Cr.Core.Loaders;

namespace a2Cr.Core.Tests.Loaders;

public sealed class LoaderIdentityTests
{
    [Fact]
    public void Describe_ReadsFabricVersionFromId()
    {
        var identity = LoaderIdentity.Describe("fabric-loader-0.16.9-1.21.1", new JsonObject
        {
            ["inheritsFrom"] = "1.21.1"
        });

        Assert.Equal("1.21.1", identity.MinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, identity.Loader);
        Assert.Equal("0.16.9", identity.LoaderVersion);
        Assert.Equal("fabric", identity.LoaderType);
    }

    [Fact]
    public void Describe_ReadsQuiltVersionFromLibraries()
    {
        var identity = LoaderIdentity.Describe("quilt-loader-0.24.0-1.21.1", new JsonObject
        {
            ["inheritsFrom"] = "1.21.1",
            ["libraries"] = new JsonArray(
                new JsonObject { ["name"] = "org.quiltmc:quilt-loader:0.24.0" })
        });

        Assert.Equal("1.21.1", identity.MinecraftVersion);
        Assert.Equal(LoaderKind.Quilt, identity.Loader);
        Assert.Equal("0.24.0", identity.LoaderVersion);
    }

    [Fact]
    public void Describe_DetectsLoaderEvenWhenVersionIdIsCustom()
    {
        var identity = LoaderIdentity.Describe("my-custom-build", new JsonObject
        {
            ["inheritsFrom"] = "1.20.1",
            ["libraries"] = new JsonArray(
                new JsonObject { ["name"] = "net.fabricmc:fabric-loader:0.15.11" })
        });

        Assert.Equal("1.20.1", identity.MinecraftVersion);
        Assert.Equal(LoaderKind.Fabric, identity.Loader);
        Assert.Equal("0.15.11", identity.LoaderVersion);
    }

    [Fact]
    public void Describe_VanillaUsesVersionIdAsMinecraftVersion()
    {
        var identity = LoaderIdentity.Describe("1.21.1", new JsonObject());

        Assert.Equal("1.21.1", identity.MinecraftVersion);
        Assert.Equal(LoaderKind.Vanilla, identity.Loader);
        Assert.Equal(string.Empty, identity.LoaderVersion);
        Assert.Equal("vanilla", identity.LoaderType);
    }
}
