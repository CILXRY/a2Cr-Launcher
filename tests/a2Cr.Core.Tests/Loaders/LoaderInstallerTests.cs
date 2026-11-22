using System.Net;
using System.Text;
using a2Cr.Core.Download;
using a2Cr.Core.Errors;
using a2Cr.Core.Loaders;
using a2Cr.Core.Meta;
using a2Cr.Core.Path;
using a2Cr.Core.Storage;

namespace a2Cr.Core.Tests.Loaders;

public sealed class LoaderInstallerTests
{
    private const string FabricList = """
        [
          { "loader": { "version": "0.20.1", "stable": false } },
          { "loader": { "version": "0.16.9", "stable": true } }
        ]
        """;

    private const string QuiltList = """
        [
          { "loader": { "version": "0.20.0-beta.9" } },
          { "loader": { "version": "0.24.0" } }
        ]
        """;

    private const string FabricProfile = """
        {
          "id": "fabric-loader-0.16.9-1.21.1",
          "inheritsFrom": "1.21.1",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "arguments": { "jvm": [], "game": [] },
          "libraries": [
            { "name": "net.fabricmc:fabric-loader:0.16.9", "url": "https://maven.fabricmc.net/" }
          ]
        }
        """;

    [Fact]
    public async Task GetLatestLoaderVersion_PrefersStableFabricBuild()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var handler = new LoaderHandler(FabricList, FabricProfile);
        var installer = CreateInstaller(paths, handler, new FakeVersionInstaller());

        var version = await installer.GetLatestLoaderVersionAsync("1.21.1", LoaderKind.Fabric,
            CancellationToken.None);

        Assert.Equal("0.16.9", version);
    }

    [Fact]
    public async Task GetLatestLoaderVersion_SkipsQuiltPrerelease()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var handler = new LoaderHandler(QuiltList, FabricProfile);
        var installer = CreateInstaller(paths, handler, new FakeVersionInstaller());

        var version = await installer.GetLatestLoaderVersionAsync("1.21.1", LoaderKind.Quilt,
            CancellationToken.None);

        Assert.Equal("0.24.0", version);
    }

    [Fact]
    public async Task InstallAsync_WritesProfileAndDownloadsLoaderLibraries()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var handler = new LoaderHandler(FabricList, FabricProfile);
        var parent = new FakeVersionInstaller();
        var installer = CreateInstaller(paths, handler, parent);

        var result = await installer.InstallAsync("1.21.1", LoaderKind.Fabric, "0.16.9", null,
            CancellationToken.None);

        Assert.Equal("fabric-loader-0.16.9-1.21.1", result.VersionId);
        Assert.Equal(["1.21.1"], parent.Installed);
        var versionPath = System.IO.Path.Combine(paths.VersionsDir, result.VersionId, $"{result.VersionId}.json");
        Assert.True(File.Exists(versionPath));
        var profile = await AtomicJson.ReadObjectAsync(versionPath, CancellationToken.None);
        Assert.Equal("1.21.1", profile!["inheritsFrom"]!.GetValue<string>());
        var library = System.IO.Path.Combine(paths.LibrariesDir, "net", "fabricmc", "fabric-loader",
            "0.16.9", "fabric-loader-0.16.9.jar");
        Assert.True(File.Exists(library));
        Assert.Equal("jar-bytes", await File.ReadAllTextAsync(library));
    }

    [Fact]
    public async Task InstallAsync_RejectsForgeUntilSupported()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var installer = CreateInstaller(paths, new LoaderHandler(FabricList, FabricProfile),
            new FakeVersionInstaller());

        var exception = await Assert.ThrowsAsync<UsageException>(() =>
            installer.InstallAsync("1.21.1", LoaderKind.Forge, null, null, CancellationToken.None));

        Assert.Equal("usage", exception.Code);
    }

    [Fact]
    public async Task InstallAsync_ReportsMetaFailureWithStableCode()
    {
        using var root = new TestRoot();
        var paths = root.CreatePaths();
        var installer = CreateInstaller(paths, new LoaderHandler(FabricList, FabricProfile, failProfile: true),
            new FakeVersionInstaller());

        var exception = await Assert.ThrowsAsync<LauncherException>(() =>
            installer.InstallAsync("1.21.1", LoaderKind.Fabric, "0.16.9", null, CancellationToken.None));

        Assert.Equal("loader_meta_failed", exception.Code);
    }

    private static LoaderInstaller CreateInstaller(LauncherPaths paths, HttpMessageHandler handler,
        IVersionInstaller parent)
    {
        var http = new HttpClient(handler);
        return new LoaderInstaller(paths, http, new DownloadService(http), parent);
    }

    private sealed class FakeVersionInstaller : IVersionInstaller
    {
        public List<string> Installed { get; } = [];

        public Task InstallAsync(string versionId, bool refresh, IProgress<DownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            Installed.Add(versionId);
            return Task.CompletedTask;
        }
    }

    private sealed class LoaderHandler : HttpMessageHandler
    {
        private readonly string _list;
        private readonly string _profile;
        private readonly bool _failProfile;

        public LoaderHandler(string list, string profile, bool failProfile = false)
        {
            _list = list;
            _profile = profile;
            _failProfile = failProfile;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/profile/json", StringComparison.Ordinal))
            {
                return Task.FromResult(_failProfile
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Json(_profile));
            }

            if (url.Contains("/versions/loader/", StringComparison.Ordinal)) return Task.FromResult(Json(_list));
            if (url.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("jar-bytes", Encoding.UTF8, "application/java-archive")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }
}
