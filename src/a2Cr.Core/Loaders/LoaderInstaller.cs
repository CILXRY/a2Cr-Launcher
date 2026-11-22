using System.Text.Json;
using System.Text.Json.Nodes;
using a2Cr.Core.Download;
using a2Cr.Core.Errors;
using a2Cr.Core.Meta;
using a2Cr.Core.Path;
using a2Cr.Core.Storage;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Loaders;

public sealed record LoaderInstallResult(
    string VersionId,
    string LoaderVersion,
    string MinecraftVersion,
    int LibraryCount);

/// <summary>
/// 安装 Fabric / Quilt。两者都直接提供带 <c>inheritsFrom</c> 的版本 JSON，
/// 启动时再和原版元数据合并。
/// </summary>
public sealed class LoaderInstaller
{
    private const string FabricListUrl = "https://meta.fabricmc.net/v2/versions/loader/{0}";
    private const string FabricProfileUrl = "https://meta.fabricmc.net/v2/versions/loader/{0}/{1}/profile/json";
    private const string QuiltListUrl = "https://meta.quiltmc.org/v3/versions/loader/{0}";
    private const string QuiltProfileUrl = "https://meta.quiltmc.org/v3/versions/loader/{0}/{1}/profile/json";

    private readonly LauncherPaths _paths;
    private readonly HttpClient _httpClient;
    private readonly DownloadService _downloader;
    private readonly IVersionInstaller _versionInstaller;

    public LoaderInstaller(
        LauncherPaths paths,
        HttpClient? httpClient = null,
        DownloadService? downloader = null,
        IVersionInstaller? versionInstaller = null)
    {
        _paths = paths;
        _httpClient = httpClient ?? new HttpClient();
        _downloader = downloader ?? new DownloadService(_httpClient);
        _versionInstaller = versionInstaller
            ?? new VersionInstaller(paths, new VersionCatalog(paths, _httpClient, _downloader), _downloader);
    }

    public async Task<string> GetLatestLoaderVersionAsync(string minecraftVersion, LoaderKind kind,
        CancellationToken cancellationToken)
    {
        var url = kind switch
        {
            LoaderKind.Fabric => string.Format(FabricListUrl, minecraftVersion),
            LoaderKind.Quilt => string.Format(QuiltListUrl, minecraftVersion),
            _ => throw Unsupported(kind)
        };

        var data = await GetJsonAsync(url, kind, cancellationToken).ConfigureAwait(false);
        if (data is not JsonArray entries || entries.Count == 0)
        {
            throw new LauncherException("loader_not_found",
                $"{kind.ToContract()} 没有找到适配 Minecraft {minecraftVersion} 的版本。",
                "确认 Minecraft 版本号是否正确，或显式指定 --loader-version。");
        }

        var versions = entries.OfType<JsonObject>()
            .Select(entry => new
            {
                Version = entry["loader"]?["version"]?.GetValue<string>(),
                Stable = entry["loader"]?["stable"]?.GetValue<bool>() ?? false
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Version))
            .ToArray();

        var selected = versions.FirstOrDefault(item => item.Stable)
                       ?? versions.FirstOrDefault(item => !IsPrerelease(item.Version!))
                       ?? versions.FirstOrDefault();
        if (selected?.Version is not { Length: > 0 } loaderVersion)
        {
            throw new LauncherException("loader_not_found",
                $"{kind.ToContract()} 的版本列表里没有可用的加载器版本。",
                "稍后重试，或显式指定 --loader-version。");
        }

        return loaderVersion;
    }

    public async Task<LoaderInstallResult> InstallAsync(
        string minecraftVersion,
        LoaderKind kind,
        string? loaderVersion,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (kind is not (LoaderKind.Fabric or LoaderKind.Quilt)) throw Unsupported(kind);
        loaderVersion ??= await GetLatestLoaderVersionAsync(minecraftVersion, kind, cancellationToken)
            .ConfigureAwait(false);

        var profileUrl = kind == LoaderKind.Fabric
            ? string.Format(FabricProfileUrl, minecraftVersion, loaderVersion)
            : string.Format(QuiltProfileUrl, minecraftVersion, loaderVersion);
        var profile = await GetJsonAsync(profileUrl, kind, cancellationToken).ConfigureAwait(false) as JsonObject
                      ?? throw new LauncherException("loader_meta_failed",
                          $"{kind.ToContract()} 返回的版本元数据不是 JSON 对象。", "稍后重试。");

        var versionId = profile["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(versionId))
        {
            versionId = $"{kind.ToContract()}-loader-{loaderVersion}-{minecraftVersion}";
            profile["id"] = versionId;
        }

        if (profile["inheritsFrom"]?.GetValue<string>() is not { Length: > 0 } parent
            || !string.Equals(parent, minecraftVersion, StringComparison.Ordinal))
        {
            profile["inheritsFrom"] = minecraftVersion;
        }

        await _versionInstaller.InstallAsync(minecraftVersion, false, progress, cancellationToken)
            .ConfigureAwait(false);

        var versionPath = IoPath.Combine(_paths.VersionsDir, versionId, $"{versionId}.json");
        await AtomicJson.WriteObjectAsync(versionPath, profile, cancellationToken).ConfigureAwait(false);

        var libraries = LibraryResolver.BuildDownloads(profile, _paths, PlatformName(), PlatformArch());
        var items = libraries.Select(library => new DownloadItem(
            library.Url,
            IoPath.Combine(_paths.LibrariesDir, library.RelativePath.Replace('/', IoPath.DirectorySeparatorChar)),
            library.Sha1,
            library.Size)).ToArray();
        if (items.Length > 0)
        {
            var report = await _downloader.DownloadAsync(items, progress: progress,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!report.AllSucceeded) throw new DownloadException(report.Failed);
        }

        return new LoaderInstallResult(versionId, loaderVersion, minecraftVersion, items.Length);
    }

    private async Task<JsonNode?> GetJsonAsync(string url, LoaderKind kind, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new LauncherException("loader_meta_failed",
                $"无法获取 {kind.ToContract()} 的版本元数据。", "检查网络或代理后重试。", exception);
        }
        catch (JsonException exception)
        {
            throw new LauncherException("loader_meta_failed",
                $"{kind.ToContract()} 返回的版本元数据无法解析。", "稍后重试。", exception);
        }
    }

    private static bool IsPrerelease(string version) =>
        version.Contains("-beta", StringComparison.OrdinalIgnoreCase)
        || version.Contains("-alpha", StringComparison.OrdinalIgnoreCase)
        || version.Contains("-rc", StringComparison.OrdinalIgnoreCase);

    private static UsageException Unsupported(LoaderKind kind) => new(
        $"暂不支持安装 {kind.ToContract()}。",
        "M2 只支持 fabric 和 quilt；forge / neoforge 将在后续里程碑提供。");

    private static string PlatformName() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux";

    private static string PlatformArch() =>
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() switch
        {
            "x64" => "x86_64",
            "x86" => "x86",
            "arm64" => "arm64",
            var value => value
        };
}
