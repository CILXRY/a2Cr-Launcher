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

    [Fact]
    public void Build_AppendsJvmArgsAfterMemory()
    {
        var paths = CreatePaths();
        var versionDir = IoPath.Combine(paths.VersionsDir, "1.20.1");
        Directory.CreateDirectory(versionDir);
        File.WriteAllBytes(IoPath.Combine(versionDir, "client.jar"), [1]);
        var meta = BaseMeta("net.minecraft.client.Main");
        meta["arguments"] = new JsonObject { ["jvm"] = new JsonArray(), ["game"] = new JsonArray() };

        var plan = new LaunchArgumentBuilder(paths, new StubJavaFinder())
            .Build(meta, "1.20.1", new LaunchOptions(IoPath.Combine(paths.RootDir, "game"),
                JvmArgs: "-Dfoo=bar -XX:+UseG1GC"));

        var arguments = plan.Arguments.ToList();
        var memoryIndex = arguments.IndexOf("-Xmx4G");
        Assert.True(memoryIndex >= 0);
        Assert.Equal(memoryIndex + 2, arguments.IndexOf("-Dfoo=bar"));
        Assert.Equal(memoryIndex + 3, arguments.IndexOf("-XX:+UseG1GC"));
    }

    [Fact]
    public void Build_RejectsJavaMajorBelowRequirement()
    {
        var paths = CreatePaths();
        var meta = BaseMeta("net.minecraft.client.Main");
        meta["arguments"] = new JsonObject { ["jvm"] = new JsonArray(), ["game"] = new JsonArray() };

        var exception = Assert.Throws<LauncherException>(() =>
            new LaunchArgumentBuilder(paths, new StubJavaFinder()).Build(meta, "1.20.1",
                new LaunchOptions(IoPath.Combine(paths.RootDir, "game"), JavaMajor: 8)));

        Assert.Equal("java_invalid", exception.Code);
    }

    [Fact]
    public void Build_UsesConfiguredJavaPathForRequestedMajor()
    {
        var paths = CreatePaths();
        var javaHome = IoPath.Combine(paths.RootDir, "java21");
        var executable = IoPath.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        Directory.CreateDirectory(IoPath.GetDirectoryName(executable)!);
        File.WriteAllBytes(executable, []);
        var versionDir = IoPath.Combine(paths.VersionsDir, "1.20.1");
        Directory.CreateDirectory(versionDir);
        File.WriteAllBytes(IoPath.Combine(versionDir, "client.jar"), [1]);
        var meta = BaseMeta("net.minecraft.client.Main");
        meta["arguments"] = new JsonObject { ["jvm"] = new JsonArray(), ["game"] = new JsonArray() };

        var plan = new LaunchArgumentBuilder(paths, new StubJavaFinder())
            .Build(meta, "1.20.1", new LaunchOptions(IoPath.Combine(paths.RootDir, "game"),
                JavaMajor: 21, JavaOverrides: new Dictionary<int, string> { [21] = javaHome }));

        Assert.Equal(executable, plan.JavaExecutable);
    }

    [Fact]
    public void SplitArguments_SplitsOnWhitespaceWithoutParsingQuotes()
    {
        // 契约 12：jvm_args 只按空白切分，引号原样留给 JVM，启动器不猜用户意图。
        Assert.Equal(["-Xmx4G", "-Dfoo=\"a", "b\"", "-Dbar=\"quoted\""],
            LaunchArgumentBuilder.SplitArguments("  -Xmx4G   -Dfoo=\"a b\" -Dbar=\"quoted\"  "));
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
