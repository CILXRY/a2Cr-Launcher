using System.Net;
using System.Security.Cryptography;
using System.Text;
using a2Cr.Core.Download;
using a2Cr.Core.Errors;
using a2Cr.Core.Instances;
using a2Cr.Core.Launcher.Definitions;
using a2Cr.Core.Mods;
using a2Cr.Core.Settings;
using a2Cr.Core.Storage;

namespace a2Cr.Core.Tests.Mods;

public sealed class ModInstallerTests
{
    [Fact]
    public async Task InstallAsync_DownloadsMainAndRequiredDependencyIntoPool()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var (manager, installer, handler) = await CreateInstallerAsync(paths, "ver-api");

        var report = await installer.InstallAsync("demo", "sodium", null, true, null, CancellationToken.None);

        Assert.Equal(2, report.Installed.Count);
        var main = Assert.Single(report.Installed, item => !item.Dependency);
        Assert.Equal("sodium.jar", main.FileName);
        var dependency = Assert.Single(report.Installed, item => item.Dependency);
        Assert.Equal("fabric-api.jar", dependency.FileName);
        Assert.Empty(report.Skipped);

        // 默认选正式版，不装 beta。
        Assert.DoesNotContain(report.Installed, item => item.FileName == "sodium-beta.jar");
        var poolDirectory = System.IO.Path.Combine(paths.ModsPoolDir, "fabric", "1.21.1");
        Assert.True(File.Exists(System.IO.Path.Combine(poolDirectory, "sodium.jar")));
        Assert.True(File.Exists(System.IO.Path.Combine(poolDirectory, "fabric-api.jar")));

        var instanceMods = System.IO.Path.Combine(manager.InstanceDirectory("demo"), "mods");
        Assert.True(File.Exists(System.IO.Path.Combine(instanceMods, "sodium.jar")));
        Assert.True(File.Exists(System.IO.Path.Combine(instanceMods, "fabric-api.jar")));
        Assert.Equal(2, report.Reconcile.Linked + report.Reconcile.Copied);

        var manifest = await InstanceManifest.LoadAsync(
            System.IO.Path.Combine(manager.InstanceDirectory("demo"), "manifest.json"), CancellationToken.None);
        Assert.Equal(FileOrigin.Pool, manifest.Find("mods/sodium.jar")!.Origin);
        Assert.Equal(FileOrigin.Pool, manifest.Find("mods/fabric-api.jar")!.Origin);
        Assert.Equal(2, handler.DownloadRequests);
    }

    [Fact]
    public async Task InstallAsync_RejectsVanillaInstance()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var (_, installer, _) = await CreateInstallerAsync(paths, "ver-api", vanilla: true);

        var exception = await Assert.ThrowsAsync<LauncherException>(() =>
            installer.InstallAsync("demo", "sodium", null, true, null, CancellationToken.None));

        Assert.Equal("mod_needs_loader", exception.Code);
    }

    [Fact]
    public async Task InstallAsync_RejectsSameFileNameWithDifferentContent()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var (_, installer, handler) = await CreateInstallerAsync(paths, "ver-api");
        var poolDirectory = System.IO.Path.Combine(paths.ModsPoolDir, "fabric", "1.21.1");
        Directory.CreateDirectory(poolDirectory);
        await File.WriteAllTextAsync(System.IO.Path.Combine(poolDirectory, "sodium.jar"), "other");

        var exception = await Assert.ThrowsAsync<StorageException>(() =>
            installer.InstallAsync("demo", "sodium", null, true, null, CancellationToken.None));

        Assert.Contains("同名", exception.Message);
        Assert.Equal(0, handler.DownloadRequests);
    }

    [Fact]
    public async Task InstallAsync_KeepsMainWhenDependencyCannotBeResolved()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var (_, installer, _) = await CreateInstallerAsync(paths, "ver-missing");

        var report = await installer.InstallAsync("demo", "sodium", null, true, null, CancellationToken.None);

        var main = Assert.Single(report.Installed);
        Assert.Equal("sodium.jar", main.FileName);
        Assert.Contains(report.Skipped, item => item.Contains("fabric-api"));
    }

    [Fact]
    public async Task ListInstalledAsync_ReadsModsAndManifestOrigin()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var (manager, installer, _) = await CreateInstallerAsync(paths, "ver-api");
        await installer.InstallAsync("demo", "sodium", null, true, null, CancellationToken.None);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(manager.InstanceDirectory("demo"), "mods", "manual.jar"), "user");

        var mods = await installer.ListInstalledAsync("demo", CancellationToken.None);

        Assert.Equal(new[] { "fabric-api.jar", "manual.jar", "sodium.jar" },
            mods.Select(item => item.FileName).ToArray());
        Assert.Equal(FileOrigin.User, mods.Single(item => item.FileName == "manual.jar").Origin);
        Assert.Equal(FileOrigin.Pool, mods.Single(item => item.FileName == "sodium.jar").Origin);
    }

    private static async Task<(InstanceManager Manager, ModInstaller Installer, ModHandler Handler)>
        CreateInstallerAsync(PathsDefinitions pathsDefinitions, string dependencyVersionId, bool vanilla = false)
    {
        var handler = new ModHandler(dependencyVersionId);
        var http = new HttpClient(handler);
        var settings = await LauncherSettings.LoadAsync(pathsDefinitions, CancellationToken.None);
        var manager = new InstanceManager(pathsDefinitions, settings);
        if (vanilla)
        {
            await manager.CreateAsync("Demo", "1.21.1", "1.21.1");
        }
        else
        {
            await manager.CreateAsync("Demo", "1.21.1", "fabric-loader-0.16.9-1.21.1", "fabric", "0.16.9");
        }

        var installer = new ModInstaller(pathsDefinitions, manager, new ModrinthClient(http), new DownloadService(http));
        return (manager, installer, handler);
    }

    private static string Sha1(string text) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string VersionList(string dependencyVersionId) => $$"""
        [
          {
            "id": "ver-beta",
            "project_id": "sodium",
            "name": "Sodium Beta",
            "version_number": "0.6.0-beta",
            "version_type": "beta",
            "game_versions": ["1.21.1"],
            "loaders": ["fabric"],
            "dependencies": [],
            "files": [
              { "filename": "sodium-beta.jar", "url": "https://cdn.invalid/sodium-beta.jar",
                "primary": true, "hashes": { "sha1": "{{Sha1("beta")}}" }, "size": 4 }
            ]
          },
          {
            "id": "ver-main",
            "project_id": "sodium",
            "name": "Sodium",
            "version_number": "0.6.1",
            "version_type": "release",
            "game_versions": ["1.21.1"],
            "loaders": ["fabric"],
            "dependencies": [
              { "project_id": "fabric-api", "version_id": "{{dependencyVersionId}}", "dependency_type": "required" }
            ],
            "files": [
              { "filename": "sodium.jar", "url": "https://cdn.invalid/sodium.jar",
                "primary": true, "hashes": { "sha1": "{{Sha1("sodium")}}" }, "size": 6 }
            ]
          }
        ]
        """;

    private static string DependencyVersion => $$"""
        {
          "id": "ver-api",
          "project_id": "fabric-api",
          "name": "Fabric API",
          "version_number": "0.100.0",
          "version_type": "release",
          "game_versions": ["1.21.1"],
          "loaders": ["fabric"],
          "dependencies": [],
          "files": [
            { "filename": "fabric-api.jar", "url": "https://cdn.invalid/fabric-api.jar",
              "primary": true, "hashes": { "sha1": "{{Sha1("api")}}" }, "size": 3 }
          ]
        }
        """;

    private sealed class ModHandler(string dependencyVersionId) : HttpMessageHandler
    {
        public int DownloadRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/project/sodium/version", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(VersionList(dependencyVersionId)));
            }

            if (url.Contains("/version/ver-api", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(DependencyVersion));
            }

            if (url.Contains("/version/ver-missing", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (url.EndsWith("/sodium.jar", StringComparison.Ordinal))
            {
                DownloadRequests++;
                return Task.FromResult(Bytes("sodium"));
            }

            if (url.EndsWith("/sodium-beta.jar", StringComparison.Ordinal))
            {
                DownloadRequests++;
                return Task.FromResult(Bytes("beta"));
            }

            if (url.EndsWith("/fabric-api.jar", StringComparison.Ordinal))
            {
                DownloadRequests++;
                return Task.FromResult(Bytes("api"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        private static HttpResponseMessage Bytes(string body) => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
        };
    }
}
