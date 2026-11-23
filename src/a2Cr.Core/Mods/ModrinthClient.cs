using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using a2Cr.Core.Errors;
using a2Cr.Core.Launcher.Definitions;

namespace a2Cr.Core.Mods;

public sealed record ModSearchHit(
    string ProjectId,
    string Slug,
    string Title,
    string Description,
    IReadOnlyList<string> Categories,
    long Downloads,
    string? IconUrl);

public sealed record ModSearchPage(int TotalHits, IReadOnlyList<ModSearchHit> Hits);

public sealed record ModFile(string FileName, string Url, string? Sha1, long Size);

public sealed record ModDependency(string? ProjectId, string? VersionId, string DependencyType);

public sealed record ModVersion(
    string Id,
    string ProjectId,
    string Name,
    string VersionNumber,
    string VersionType,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    IReadOnlyList<ModDependency> Dependencies,
    ModFile? PrimaryFile);

/// <summary>Modrinth v2 的最小客户端；只做搜索、版本和文件解析。</summary>
public sealed class ModrinthClient
{
    public string UserAgent = LauncherDefinitions.UserAgent;

    private const string BaseUrl = "https://api.modrinth.com/v2";
    private readonly HttpClient _httpClient;

    public ModrinthClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Remove("User-Agent");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    public async Task<ModSearchPage> SearchAsync(string query, string? mcVersion, string? loader,
        int limit, int offset, CancellationToken cancellationToken)
    {
        var facets = new List<List<string>> { new() { "project_type:mod" } };
        if (!string.IsNullOrWhiteSpace(mcVersion)) facets.Add([$"versions:{mcVersion}"]);
        if (!string.IsNullOrWhiteSpace(loader)) facets.Add([$"categories:{loader}"]);
        var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query)}"
                  + $"&limit={Math.Clamp(limit, 1, 100)}&offset={Math.Max(0, offset)}&index=relevance"
                  + $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}";
        var root = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false) as JsonObject
                   ?? throw new LauncherException("modrinth_failed", "Modrinth 搜索返回的不是 JSON 对象。");
        var hits = new List<ModSearchHit>();
        if (root["hits"] is JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                hits.Add(new ModSearchHit(
                    item["project_id"]?.GetValue<string>() ?? string.Empty,
                    item["slug"]?.GetValue<string>() ?? string.Empty,
                    item["title"]?.GetValue<string>() ?? string.Empty,
                    item["description"]?.GetValue<string>() ?? string.Empty,
                    ReadStrings(item["categories"]),
                    item["downloads"]?.GetValue<long>() ?? 0,
                    item["icon_url"]?.GetValue<string>()));
            }
        }

        return new ModSearchPage(root["total_hits"]?.GetValue<int>() ?? hits.Count, hits);
    }

    public async Task<IReadOnlyList<ModVersion>> GetVersionsAsync(string project, string? mcVersion,
        string? loader, CancellationToken cancellationToken)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(mcVersion))
        {
            query.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { mcVersion })));
        }

        if (!string.IsNullOrWhiteSpace(loader))
        {
            query.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader })));
        }

        var url = $"{BaseUrl}/project/{Uri.EscapeDataString(project)}/version";
        if (query.Count > 0) url += "?" + string.Join('&', query);
        var root = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (root is not JsonArray items)
        {
            throw new LauncherException("modrinth_failed", "Modrinth 版本列表不是 JSON 数组。");
        }

        return items.OfType<JsonObject>().Select(ToVersion).ToArray();
    }

    public async Task<ModVersion> GetVersionAsync(string versionId, CancellationToken cancellationToken)
    {
        var root = await GetJsonAsync($"{BaseUrl}/version/{Uri.EscapeDataString(versionId)}", cancellationToken)
            .ConfigureAwait(false) as JsonObject
            ?? throw new LauncherException("modrinth_failed", "Modrinth 版本数据不是 JSON 对象。");
        return ToVersion(root);
    }

    private async Task<JsonNode> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new LauncherException("mod_not_found", "Modrinth 上找不到对应的项目或版本。",
                    "检查项目名或版本 id 是否正确。");
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
                   ?? throw new LauncherException("modrinth_failed", "Modrinth 返回了空响应。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new LauncherException("modrinth_failed", "无法连接 Modrinth。", "检查网络或代理后重试。", exception);
        }
        catch (JsonException exception)
        {
            throw new LauncherException("modrinth_failed", "Modrinth 返回的数据无法解析。", "稍后重试。", exception);
        }
    }

    private static ModVersion ToVersion(JsonObject item)
    {
        ModFile? primary = null;
        if (item["files"] is JsonArray files)
        {
            var chosen = files.OfType<JsonObject>().FirstOrDefault(file => file["primary"]?.GetValue<bool>() == true)
                         ?? files.OfType<JsonObject>().FirstOrDefault();
            if (chosen is not null)
            {
                primary = new ModFile(
                    chosen["filename"]?.GetValue<string>() ?? "download.jar",
                    chosen["url"]?.GetValue<string>() ?? string.Empty,
                    chosen["hashes"]?["sha1"]?.GetValue<string>(),
                    chosen["size"]?.GetValue<long>() ?? 0);
            }
        }

        var dependencies = new List<ModDependency>();
        if (item["dependencies"] is JsonArray dependencyNodes)
        {
            foreach (var dependency in dependencyNodes.OfType<JsonObject>())
            {
                dependencies.Add(new ModDependency(
                    dependency["project_id"]?.GetValue<string>(),
                    dependency["version_id"]?.GetValue<string>(),
                    dependency["dependency_type"]?.GetValue<string>() ?? string.Empty));
            }
        }

        return new ModVersion(
            item["id"]?.GetValue<string>() ?? string.Empty,
            item["project_id"]?.GetValue<string>() ?? string.Empty,
            item["name"]?.GetValue<string>() ?? string.Empty,
            item["version_number"]?.GetValue<string>() ?? string.Empty,
            item["version_type"]?.GetValue<string>() ?? string.Empty,
            ReadStrings(item["game_versions"]),
            ReadStrings(item["loaders"]),
            dependencies,
            primary);
    }

    private static IReadOnlyList<string> ReadStrings(JsonNode? node)
    {
        if (node is not JsonArray array) return [];
        return array.OfType<JsonValue>().Select(value => value.GetValue<string>()).ToArray();
    }
}
