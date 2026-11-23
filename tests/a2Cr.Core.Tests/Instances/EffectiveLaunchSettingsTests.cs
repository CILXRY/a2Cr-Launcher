using System.Text.Json.Nodes;
using a2Cr.Core.Instances;
using a2Cr.Core.Settings;

namespace a2Cr.Core.Tests.Instances;

public sealed class EffectiveLaunchSettingsTests
{
    [Fact]
    public async Task Resolve_PrefersExplicitThenInstanceThenGlobal()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var settings = await LauncherSettings.LoadAsync(paths, CancellationToken.None);
        var metadata = InstanceMetadata.Create("demo", "Demo", "1.21.1", "1.21.1");
        metadata.Root["overrides"] = new JsonObject
        {
            ["max_memory"] = "8G",
            ["jvm_args"] = "-XX:+UseG1GC",
            ["java_major"] = 21
        };

        var inherited = EffectiveLaunchSettings.Resolve(new LaunchOverrides(), metadata, settings);
        Assert.Equal("8G", inherited.MaxMemory);
        Assert.Equal("-XX:+UseG1GC", inherited.JvmArgs);
        Assert.Equal(21, inherited.JavaMajor);

        var explicitValues = EffectiveLaunchSettings.Resolve(
            new LaunchOverrides("6G", "-Dfoo=bar", 17, "Steve"), metadata, settings);
        Assert.Equal("6G", explicitValues.MaxMemory);
        Assert.Equal("-Dfoo=bar", explicitValues.JvmArgs);
        Assert.Equal(17, explicitValues.JavaMajor);
        Assert.Equal("Steve", explicitValues.PlayerName);
    }

    [Fact]
    public async Task Resolve_FallsBackToGlobalWhenInstanceHasNoOverrides()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var settings = await LauncherSettings.LoadAsync(paths, CancellationToken.None);
        var metadata = InstanceMetadata.Create("demo", "Demo", "1.21.1", "1.21.1");

        var effective = EffectiveLaunchSettings.Resolve(new LaunchOverrides(), metadata, settings);

        Assert.Equal("4G", effective.MaxMemory);
        Assert.Equal(string.Empty, effective.JvmArgs);
        Assert.Null(effective.JavaMajor);
        Assert.Equal("a2Cr", effective.PlayerName);
    }

    [Fact]
    public async Task Resolve_IgnoresInvalidExplicitMemory()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var settings = await LauncherSettings.LoadAsync(paths, CancellationToken.None);
        var metadata = InstanceMetadata.Create("demo", "Demo", "1.21.1", "1.21.1");
        metadata.Root["overrides"] = new JsonObject { ["max_memory"] = "8G" };

        var effective = EffectiveLaunchSettings.Resolve(new LaunchOverrides("abc"), metadata, settings);

        Assert.Equal("8G", effective.MaxMemory);
    }
}
