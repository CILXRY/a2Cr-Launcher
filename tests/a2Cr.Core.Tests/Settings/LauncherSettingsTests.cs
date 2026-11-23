using a2Cr.Core.Settings;
using a2Cr.Core.Storage;

namespace a2Cr.Core.Tests.Settings;

public sealed class LauncherSettingsTests
{
    [Fact]
    public async Task Load_CreatesDefaultsWhenFileIsMissing()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();

        var settings = await LauncherSettings.LoadAsync(paths, CancellationToken.None);

        Assert.True(File.Exists(System.IO.Path.Combine(paths.ConfigsDir, "launcher.json")));
        Assert.Equal("4G", settings.MaxMemory);
        Assert.Equal(8, settings.MaxConcurrency);
        Assert.Equal(4, settings.MaxRetry);
        Assert.Equal("slug", settings.InstanceIdStyle);
        Assert.False(settings.CloseAfterLaunch);
        Assert.Null(settings.ProxyUrl);
    }

    [Fact]
    public async Task Load_FallsBackAndClampsInvalidValues()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var path = System.IO.Path.Combine(paths.ConfigsDir, "launcher.json");
        await File.WriteAllTextAsync(path, """
            {
              "format": 1,
              "launch": { "max_memory": "abc" },
              "download": { "max_concurrency": 99, "max_retry": -5 },
              "storage": { "instance_id_style": "weird" }
            }
            """);

        var settings = await LauncherSettings.LoadAsync(paths, CancellationToken.None);

        Assert.Equal("4G", settings.MaxMemory);
        Assert.Equal(16, settings.MaxConcurrency);
        Assert.Equal(0, settings.MaxRetry);
        Assert.Equal("slug", settings.InstanceIdStyle);
    }

    [Fact]
    public async Task Load_PreservesUnknownFields()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var path = System.IO.Path.Combine(paths.ConfigsDir, "launcher.json");
        await File.WriteAllTextAsync(path, "{\"format\":1,\"future\":{\"keep\":true}}");

        var settings = await LauncherSettings.LoadAsync(paths, CancellationToken.None);
        await settings.SaveAsync(paths, CancellationToken.None);

        var saved = await AtomicJson.ReadObjectAsync(path, CancellationToken.None);
        Assert.True(saved!["future"]!["keep"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Load_ReadsCustomProxyAndJavaOverrides()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var path = System.IO.Path.Combine(paths.ConfigsDir, "launcher.json");
        await File.WriteAllTextAsync(path, """
            {
              "format": 1,
              "java": { "overrides": { "21": "C:\\Java\\21\\bin\\java.exe" }, "extra_paths": ["D:\\Java"] },
              "download": { "proxy": { "mode": "custom", "url": "http://127.0.0.1:7890" } }
            }
            """);

        var settings = await LauncherSettings.LoadAsync(paths, CancellationToken.None);

        Assert.Equal("http://127.0.0.1:7890", settings.ProxyUrl);
        Assert.Equal("C:\\Java\\21\\bin\\java.exe", settings.JavaOverrides[21]);
        Assert.Equal(["D:\\Java"], settings.ExtraJavaPaths);
    }
}
