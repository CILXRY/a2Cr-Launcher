using System.Net;
using System.Text;
using a2Cr.Core.Meta;
using a2Cr.Core.Path;

namespace a2Cr.Core.Tests.Meta;

public sealed class VersionCatalogTests
{
    [Fact]
    public async Task List_UsesCachedManifestWithoutHttpRequest()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.MetaDir);
        await File.WriteAllTextAsync(paths.VersionManifestPath,
            "{\"versions\":[{\"id\":\"1.21.1\",\"type\":\"release\",\"releaseTime\":\"2024-09-19T00:00:00Z\",\"url\":\"https://test.invalid/1.21.1.json\"}]}"
        );
        var handler = new CountingHandler();
        var catalog = new VersionCatalog(paths, new HttpClient(handler));

        var versions = await catalog.ListAsync(false, CancellationToken.None);

        Assert.Single(versions);
        Assert.Equal("1.21.1", versions[0].Id);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task List_RefreshesManifestAtomically()
    {
        var paths = CreatePaths();
        var content = "{\"versions\":[{\"id\":\"1.20.1\",\"type\":\"release\",\"url\":\"https://test.invalid/1.20.1.json\"}]}";
        var catalog = new VersionCatalog(paths, new HttpClient(new CountingHandler(content)));

        var versions = await catalog.ListAsync(true, CancellationToken.None);

        Assert.Equal("1.20.1", Assert.Single(versions).Id);
        Assert.True(File.Exists(paths.VersionManifestPath));
        Assert.False(File.Exists(paths.VersionManifestPath + ".part"));
    }

    [Fact]
    public async Task Get_ReadsLocalLoaderProfileWithoutManifestEntry()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.MetaDir);
        await File.WriteAllTextAsync(paths.VersionManifestPath, "{\"versions\":[]}");
        var versionId = "fabric-loader-0.16.9-1.21.1";
        var versionDir = System.IO.Path.Combine(paths.VersionsDir, versionId);
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(System.IO.Path.Combine(versionDir, versionId + ".json"),
            $"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"1.21.1\"}}");
        var handler = new CountingHandler();
        var catalog = new VersionCatalog(paths, new HttpClient(handler));

        var meta = await catalog.GetAsync(versionId, false, CancellationToken.None);

        Assert.Equal("1.21.1", meta["inheritsFrom"]!.GetValue<string>());
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task List_ReportsCorruptCacheAsMetaIncomplete()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.MetaDir);
        await File.WriteAllTextAsync(paths.VersionManifestPath, "{broken");
        var catalog = new VersionCatalog(paths, new HttpClient(new CountingHandler()));

        var exception = await Assert.ThrowsAsync<a2Cr.Core.Errors.MetaIncompleteException>(
            () => catalog.ListAsync(false, CancellationToken.None));

        Assert.Equal("meta_incomplete", exception.Code);
    }

    private static LauncherPaths CreatePaths() => new(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "a2cr-catalog-tests", Guid.NewGuid().ToString("N")));

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly string _content;

        public CountingHandler(string content = "{}")
        {
            _content = content;
        }

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            });
        }
    }
}
