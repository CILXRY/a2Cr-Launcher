using System.Text.Json.Nodes;
using a2Cr.Core.Errors;
using a2Cr.Core.Launch;
using a2Cr.Core.Path;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Tests.Launch;

public sealed class LaunchArgumentsTests
{
    [Fact]
    public void Build_ReplacesArgumentsAndAddsClientJar()
    {
        var paths = CreatePaths();
        var versionDir = IoPath.Combine(paths.VersionsDir, "1.20.1");
        Directory.CreateDirectory(versionDir);
        File.WriteAllBytes(IoPath.Combine(versionDir, "client.jar"), [1]);
        var meta = BaseMeta("net.minecraft.client.Main");
        meta["arguments"] = new JsonObject
        {
            ["jvm"] = new JsonArray("-Dgame=${game_directory}"),
            ["game"] = new JsonArray("--username", "${auth_player_name}")
        };

        var plan = new LaunchArgumentBuilder(paths, new StubJavaFinder())
            .Build(meta, "1.20.1", new LaunchOptions(IoPath.Combine(paths.RootDir, "game")));

        Assert.Contains("-Dgame=" + IoPath.Combine(paths.RootDir, "game"), plan.Arguments);
        Assert.Contains("a2Cr", plan.Arguments);
        Assert.Contains(IoPath.Combine(versionDir, "client.jar"), plan.Classpath);
        Assert.Equal("C:\\java-test\\bin\\java.exe", plan.JavaExecutable);
    }

    [Fact]
    public void Build_UnknownPlaceholder_IsMetaIncomplete()
    {
        var paths = CreatePaths();
        var versionDir = IoPath.Combine(paths.VersionsDir, "1.20.1");
        Directory.CreateDirectory(versionDir);
        File.WriteAllBytes(IoPath.Combine(versionDir, "client.jar"), [1]);
        var meta = BaseMeta("net.minecraft.client.Main");
        meta["arguments"] = new JsonObject
        {
            ["jvm"] = new JsonArray("-Dunknown=${not_defined}")
        };

        var exception = Assert.Throws<MetaIncompleteException>(() =>
            new LaunchArgumentBuilder(paths, new StubJavaFinder()).Build(meta, "1.20.1",
                new LaunchOptions(IoPath.Combine(paths.RootDir, "game"))));

        Assert.Equal("meta_incomplete", exception.Code);
    }

    [Fact]
    public void Build_BootstrapLauncher_DoesNotAddClientJar()
    {
        var paths = CreatePaths();
        var versionDir = IoPath.Combine(paths.VersionsDir, "1.21.1");
        Directory.CreateDirectory(versionDir);
        File.WriteAllBytes(IoPath.Combine(versionDir, "client.jar"), [1]);
        var meta = BaseMeta("cpw.mods.bootstraplauncher.BootstrapLauncher");
        meta["arguments"] = new JsonObject();

        var plan = new LaunchArgumentBuilder(paths, new StubJavaFinder())
            .Build(meta, "1.21.1", new LaunchOptions(IoPath.Combine(paths.RootDir, "game")));

        Assert.DoesNotContain(plan.Classpath, path => path.EndsWith("client.jar", StringComparison.OrdinalIgnoreCase));
    }

    private static JsonObject BaseMeta(string mainClass) => new()
    {
        ["id"] = "1.20.1",
        ["type"] = "release",
        ["mainClass"] = mainClass,
        ["assetIndex"] = new JsonObject { ["id"] = "legacy" },
        ["javaVersion"] = new JsonObject { ["majorVersion"] = 17 },
        ["libraries"] = new JsonArray()
    };

    private static LauncherPaths CreatePaths() => new(
        IoPath.Combine(IoPath.GetTempPath(), "a2cr-launch-tests", Guid.NewGuid().ToString("N")));

    private sealed class StubJavaFinder : a2Cr.Core.Java.JavaRuntimeFinder
    {
        public override a2Cr.Core.Java.JavaRuntime Select(int requiredMajor, IEnumerable<string>? extraPaths = null) =>
            new("C:\\java-test", requiredMajor, requiredMajor.ToString());
    }
}
