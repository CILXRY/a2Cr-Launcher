using System.Net;
using System.Text;
using a2Cr.Core.Mods;

namespace a2Cr.Core.Tests.Mods;

public sealed class ModrinthClientTests
{
    [Fact]
    public async Task SearchAsync_SendsFacetsAndReadsTotalHits()
    {
        var handler = new CaptureHandler(_ => Json("""
            {
              "total_hits": 42,
              "hits": [
                {
                  "project_id": "sodium-id",
                  "slug": "sodium",
                  "title": "Sodium",
                  "description": "渲染优化",
                  "categories": ["optimization", "fabric"],
                  "downloads": 1234,
                  "icon_url": "https://cdn.invalid/sodium.png"
                }
              ]
            }
            """));
        var client = new ModrinthClient(new HttpClient(handler));

        var page = await client.SearchAsync("sodium", "1.21.1", "fabric", 10, 0, CancellationToken.None);

        Assert.Equal(42, page.TotalHits);
        var hit = Assert.Single(page.Hits);
        Assert.Equal("sodium", hit.Slug);
        Assert.Equal("渲染优化", hit.Description);
        Assert.Equal(1234, hit.Downloads);
        var url = Assert.Single(handler.Requests);
        var decoded = Uri.UnescapeDataString(url.Query);
        Assert.Contains("facets=[[\"project_type:mod\"],[\"versions:1.21.1\"],[\"categories:fabric\"]]", decoded);
        Assert.Contains("index=relevance", decoded);
    }

    [Fact]
    public async Task SearchAsync_SendsCustomUserAgent()
    {
        var handler = new CaptureHandler(_ => Json("{\"total_hits\":0,\"hits\":[]}"));
        var client = new ModrinthClient(new HttpClient(handler));

        await client.SearchAsync("x", null, null, 10, 0, CancellationToken.None);

        var userAgent = Assert.Single(handler.UserAgents);
        Assert.Contains("a2cr-launcher", userAgent);
        Assert.Contains("github.com/CILXRY/a2Cr-Launcher", userAgent);
    }

    [Fact]
    public async Task GetVersionsAsync_PrefersPrimaryFile()
    {
        var handler = new CaptureHandler(_ => Json("""
            [
              {
                "id": "ver-1",
                "project_id": "sodium",
                "name": "Sodium",
                "version_number": "0.6.1",
                "version_type": "release",
                "game_versions": ["1.21.1"],
                "loaders": ["fabric"],
                "dependencies": [],
                "files": [
                  { "filename": "sources.jar", "url": "https://cdn.invalid/sources.jar", "primary": false,
                    "hashes": { "sha1": "aa" }, "size": 1 },
                  { "filename": "sodium.jar", "url": "https://cdn.invalid/sodium.jar", "primary": true,
                    "hashes": { "sha1": "bb" }, "size": 2 }
                ]
              }
            ]
            """));
        var client = new ModrinthClient(new HttpClient(handler));

        var versions = await client.GetVersionsAsync("sodium", "1.21.1", "fabric", CancellationToken.None);

        var version = Assert.Single(versions);
        Assert.NotNull(version.PrimaryFile);
        Assert.Equal("sodium.jar", version.PrimaryFile.FileName);
        Assert.Equal("bb", version.PrimaryFile.Sha1);
        var decoded = Uri.UnescapeDataString(Assert.Single(handler.Requests).Query);
        Assert.Contains("game_versions=[\"1.21.1\"]", decoded);
        Assert.Contains("loaders=[\"fabric\"]", decoded);
    }

    [Fact]
    public async Task GetVersionsAsync_FallsBackToFirstFileWithoutPrimary()
    {
        var handler = new CaptureHandler(_ => Json("""
            [
              {
                "id": "ver-1", "project_id": "sodium", "name": "Sodium", "version_number": "1",
                "version_type": "release", "game_versions": [], "loaders": [], "dependencies": [],
                "files": [
                  { "filename": "first.jar", "url": "https://cdn.invalid/first.jar",
                    "hashes": { "sha1": "aa" }, "size": 1 }
                ]
              }
            ]
            """));
        var client = new ModrinthClient(new HttpClient(handler));

        var versions = await client.GetVersionsAsync("sodium", null, null, CancellationToken.None);

        Assert.Equal("first.jar", Assert.Single(versions).PrimaryFile!.FileName);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            return Task.FromResult(respond(request));
        }
    }
}
