using System.Text.Json.Nodes;
using a2Cr.Core.Errors;
using a2Cr.Core.Launcher.Definitions;
using a2Cr.Core.Storage;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Loaders;

public sealed record VersionIdentity(
    string VersionId,
    string MinecraftVersion,
    LoaderKind Loader,
    string LoaderVersion)
{
    public string LoaderType => Loader.ToContract();
}

/// <summary>从已安装的版本 JSON 反推原版版本号和加载器，供实例元数据使用。</summary>
public static class LoaderIdentity
{
    public static async Task<VersionIdentity> ReadAsync(PathsDefinitions paths, string versionId,
        CancellationToken cancellationToken)
    {
        var path = IoPath.Combine(paths.VersionsDir, versionId, $"{versionId}.json");
        if (!File.Exists(path))
        {
            throw new LauncherException("version_unknown", $"版本 {versionId} 还没有安装。",
                $"先运行 a2Cr install {versionId}。");
        }

        var root = await AtomicJson.ReadObjectAsync(path, cancellationToken).ConfigureAwait(false)
                   ?? throw new MetaIncompleteException($"版本 {versionId} 的元数据为空。");
        return Describe(versionId, root);
    }

    public static VersionIdentity Describe(string versionId, JsonObject metadata)
    {
        var minecraftVersion = metadata["inheritsFrom"]?.GetValue<string>();
        var loader = DetectLoader(metadata, versionId, out var loaderVersion);
        return new VersionIdentity(versionId,
            string.IsNullOrWhiteSpace(minecraftVersion) ? versionId : minecraftVersion,
            loader, loaderVersion);
    }

    private static LoaderKind DetectLoader(JsonObject metadata, string versionId, out string loaderVersion)
    {
        if (metadata["libraries"] is JsonArray libraries)
        {
            foreach (var library in libraries.OfType<JsonObject>())
            {
                var name = library["name"]?.GetValue<string>();
                if (name is null) continue;
                if (name.StartsWith("net.fabricmc:fabric-loader:", StringComparison.Ordinal))
                {
                    loaderVersion = name["net.fabricmc:fabric-loader:".Length..];
                    return LoaderKind.Fabric;
                }

                if (name.StartsWith("org.quiltmc:quilt-loader:", StringComparison.Ordinal))
                {
                    loaderVersion = name["org.quiltmc:quilt-loader:".Length..];
                    return LoaderKind.Quilt;
                }
            }
        }

        if (TryParseId(versionId, "fabric-loader-", metadata, out loaderVersion)) return LoaderKind.Fabric;
        if (TryParseId(versionId, "quilt-loader-", metadata, out loaderVersion)) return LoaderKind.Quilt;
        loaderVersion = string.Empty;
        return LoaderKind.Vanilla;
    }

    private static bool TryParseId(string versionId, string prefix, JsonObject metadata, out string loaderVersion)
    {
        loaderVersion = string.Empty;
        if (!versionId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = versionId[prefix.Length..];
        var parent = metadata["inheritsFrom"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(parent))
        {
            var suffix = "-" + parent;
            if (rest.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) rest = rest[..^suffix.Length];
        }

        loaderVersion = rest;
        return loaderVersion.Length > 0;
    }
}
