using System.Text.Json;
using System.Text.Json.Nodes;
using a2Cr.Core.Download;
using a2Cr.Core.Errors;
using a2Cr.Core.Launch;
using a2Cr.Core.Path;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Meta;

public sealed record VersionSummary(string Id, string Type, DateTimeOffset? ReleaseTime, string Url);

public sealed class VersionCatalog
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private readonly LauncherPaths _paths;
    private readonly HttpClient _httpClient;
    private readonly DownloadService _downloader;

    public VersionCatalog(LauncherPaths paths, HttpClient? httpClient = null, DownloadService? downloader = null)
    {
        _paths = paths;
        _httpClient = httpClient ?? new HttpClient();
        _downloader = downloader ?? new DownloadService(_httpClient);
    }

    public async Task<IReadOnlyList<VersionSummary>> ListAsync(bool refresh, CancellationToken cancellationToken)
    {
        var manifest = await LoadManifestAsync(refresh, cancellationToken).ConfigureAwait(false);
        if (manifest["versions"] is not JsonArray versions)
        {
            throw new LauncherException("meta_incomplete", "版本清单缺少 versions 字段。");
        }

        return versions.OfType<JsonObject>().Select(item => new VersionSummary(
            item["id"]?.GetValue<string>() ?? string.Empty,
            item["type"]?.GetValue<string>() ?? "release",
            ParseTime(item["releaseTime"]?.GetValue<string>()),
            item["url"]?.GetValue<string>() ?? string.Empty)).Where(item => item.Id.Length > 0).ToArray();
    }

    public async Task<JsonObject> GetAsync(string versionId, bool refresh, CancellationToken cancellationToken)
    {
        var summary = (await ListAsync(refresh, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, versionId, StringComparison.Ordinal));
        if (summary is null)
        {
            throw new LauncherException("version_unknown", $"找不到版本 {versionId}。", "请先用 version 查看可用版本。");
        }

        var versionDirectory = IoPath.Combine(_paths.VersionsDir, versionId);
        var versionPath = IoPath.Combine(versionDirectory, $"{versionId}.json");
        if (!refresh && File.Exists(versionPath))
        {
            return await ReadObjectAsync(versionPath, cancellationToken).ConfigureAwait(false);
        }

        Directory.CreateDirectory(versionDirectory);
        var response = await _httpClient.GetAsync(summary.Url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(versionPath, bytes, cancellationToken).ConfigureAwait(false);
        return JsonNode.Parse(bytes)?.AsObject() ?? throw new LauncherException("meta_incomplete", $"版本 {versionId} 的元数据不是对象。");
    }

    private async Task<JsonObject> LoadManifestAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (refresh || !File.Exists(_paths.VersionManifestPath))
        {
            Directory.CreateDirectory(_paths.MetaDir);
            var response = await _httpClient.GetAsync(ManifestUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            await WriteAtomicAsync(_paths.VersionManifestPath, bytes, cancellationToken).ConfigureAwait(false);
        }

        return await ReadObjectAsync(_paths.VersionManifestPath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonObject> ReadObjectAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject
                ?? throw new LauncherException("meta_incomplete", $"元数据文件不是 JSON 对象：{path}");
        }
        catch (JsonException exception)
        {
            throw new MetaIncompleteException($"元数据文件损坏：{path}") { Source = exception.Source };
        }
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporary = path + ".part";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, out var result) ? result : null;
}

public sealed class VersionInstaller : IVersionInstaller
{
    private readonly LauncherPaths _paths;
    private readonly VersionCatalog _catalog;
    private readonly DownloadService _downloader;

    public VersionInstaller(LauncherPaths paths, VersionCatalog catalog, DownloadService downloader)
    {
        _paths = paths;
        _catalog = catalog;
        _downloader = downloader;
    }

    public async Task InstallAsync(string versionId, bool refresh, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var raw = await _catalog.GetAsync(versionId, refresh, cancellationToken).ConfigureAwait(false);
        await EnsureInheritedMetadataAsync(raw, refresh, cancellationToken).ConfigureAwait(false);
        var merged = await new VersionMetaMerger(_paths).ResolveAsync(versionId, cancellationToken).ConfigureAwait(false);
        var items = new List<DownloadItem>();
        AddVersionDownloads(merged, versionId, merged["_baseVersion"]?.GetValue<string>() ?? versionId, items);
        AddLibraries(merged, items);
        AddNatives(merged, items);
        if (items.Count == 0) throw new LauncherException("meta_incomplete", $"版本 {versionId} 没有可下载的文件。");
        await DownloadOrThrowAsync(items, progress, cancellationToken).ConfigureAwait(false);

        ExtractNatives(merged, versionId);

        var assets = new List<DownloadItem>();
        AddAssets(merged, assets);
        await DownloadOrThrowAsync(assets, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInheritedMetadataAsync(JsonObject meta, bool refresh, CancellationToken cancellationToken)
    {
        var parentId = meta["inheritsFrom"]?.GetValue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(parentId) && visited.Add(parentId))
        {
            var parent = await _catalog.GetAsync(parentId, refresh, cancellationToken).ConfigureAwait(false);
            parentId = parent["inheritsFrom"]?.GetValue<string>();
        }

        if (!string.IsNullOrWhiteSpace(parentId))
        {
            throw new MetaIncompleteException($"版本继承出现循环：{versionIdForMessage(meta)} -> {parentId}");
        }
    }

    private static string versionIdForMessage(JsonObject meta) =>
        meta["id"]?.GetValue<string>() ?? "未知版本";

    private async Task DownloadOrThrowAsync(IReadOnlyList<DownloadItem> items, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0) return;
        var report = await _downloader.DownloadAsync(items, progress: progress, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!report.AllSucceeded) throw new DownloadException(report.Failed);
    }

    private void AddVersionDownloads(JsonObject meta, string versionId, string baseVersion,
        ICollection<DownloadItem> items)
    {
        var client = meta["downloads"]?["client"] as JsonObject;
        if (client?["url"]?.GetValue<string>() is { } url && client["sha1"]?.GetValue<string>() is { } sha1)
        {
            items.Add(new DownloadItem(url, IoPath.Combine(_paths.VersionsDir, baseVersion, "client.jar"), sha1,
                client["size"]?.GetValue<long>() ?? 0));
        }
        var assetIndex = meta["assetIndex"] as JsonObject;
        if (assetIndex?["url"]?.GetValue<string>() is { } indexUrl && assetIndex["id"]?.GetValue<string>() is { } indexId)
        {
            items.Add(new DownloadItem(indexUrl, IoPath.Combine(_paths.AssetsIndexesDir, indexId + ".json"),
                assetIndex["sha1"]?.GetValue<string>(), assetIndex["size"]?.GetValue<long>() ?? 0));
        }
    }

    private void AddLibraries(JsonObject meta, ICollection<DownloadItem> items)
    {
        foreach (var library in LibraryResolver.BuildDownloads(meta, _paths, PlatformName(), PlatformArch()))
        {
            items.Add(new DownloadItem(library.Url, IoPath.Combine(_paths.LibrariesDir, library.RelativePath.Replace('/', IoPath.DirectorySeparatorChar)),
                library.Sha1, library.Size));
        }
    }

    private void AddNatives(JsonObject meta, ICollection<DownloadItem> items)
    {
        foreach (var native in LibraryResolver.BuildNativeDownloads(meta, PlatformName(), PlatformArch()))
        {
            items.Add(new DownloadItem(native.Url,
                IoPath.Combine(_paths.LibrariesDir, native.RelativePath.Replace('/', IoPath.DirectorySeparatorChar)),
                native.Sha1, native.Size));
        }
    }

    private void ExtractNatives(JsonObject meta, string versionId)
    {
        var nativesDirectory = IoPath.Combine(_paths.VersionsDir, versionId, "natives");
        foreach (var native in LibraryResolver.BuildNativeDownloads(meta, PlatformName(), PlatformArch()))
        {
            var archive = IoPath.Combine(_paths.LibrariesDir,
                native.RelativePath.Replace('/', IoPath.DirectorySeparatorChar));
            NativeLibraryExtractor.Extract(archive, nativesDirectory);
        }
    }

    private void AddAssets(JsonObject meta, ICollection<DownloadItem> items)
    {
        var indexId = meta["assetIndex"]?["id"]?.GetValue<string>();
        if (indexId is null) return;
        var indexPath = IoPath.Combine(_paths.AssetsIndexesDir, indexId + ".json");
        if (!File.Exists(indexPath)) return;
        var index = JsonNode.Parse(File.ReadAllBytes(indexPath)) as JsonObject;
        if (index?["objects"] is not JsonObject objects) return;
        foreach (var item in objects)
        {
            var hash = item.Value?["hash"]?.GetValue<string>();
            var size = item.Value?["size"]?.GetValue<long>() ?? 0;
            if (hash is null) continue;
            items.Add(new DownloadItem($"https://resources.download.minecraft.net/{hash[..2]}/{hash}",
                IoPath.Combine(_paths.AssetsObjectsDir, hash[..2], hash), hash, size));
        }
    }

    private static string PlatformName() => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";
    private static string PlatformArch() => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() switch
    {
        "x64" => "x86_64",
        "x86" => "x86",
        "arm64" => "arm64",
        var value => value
    };
}

