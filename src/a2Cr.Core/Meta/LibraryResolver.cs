using a2Cr.Core.Path;

namespace a2Cr.Core.Meta;

public sealed record LibraryArtifact(string Url, string RelativePath, string? Sha1, long Size);

public static class LibraryResolver
{
    private const string DefaultMaven = "https://libraries.minecraft.net/";

    public static string? GetRelativePath(System.Text.Json.Nodes.JsonObject library)
    {
        var artifactPath = library["downloads"]?["artifact"]?["path"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(artifactPath)) return artifactPath;

        var name = library["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name)) return null;
        var parts = name.Split(':');
        if (parts.Length < 3) return null;
        var classifier = parts.Length > 3 ? $"-{parts[3]}" : string.Empty;
        return string.Join('/', parts[0].Split('.').Append(parts[1]).Append(parts[2]))
            + $"/{parts[1]}-{parts[2]}{classifier}.jar";
    }

    public static IReadOnlyList<LibraryArtifact> BuildDownloads(
        System.Text.Json.Nodes.JsonObject meta,
        LauncherPaths paths,
        string osName,
        string osArch)
    {
        var result = new List<LibraryArtifact>();
        if (meta["libraries"] is not System.Text.Json.Nodes.JsonArray libraries) return result;
        foreach (var node in libraries)
        {
            if (node is not System.Text.Json.Nodes.JsonObject library
                || !RuleEvaluator.IsAllowed(library["rules"], osName, osArch)) continue;
            var relativePath = GetRelativePath(library);
            if (relativePath is null) continue;

            var artifact = library["downloads"]?["artifact"] as System.Text.Json.Nodes.JsonObject;
            var url = artifact?["url"]?.GetValue<string>();
            var sha1 = artifact?["sha1"]?.GetValue<string>() ?? library["sha1"]?.GetValue<string>();
            var size = artifact?["size"]?.GetValue<long>() ?? library["size"]?.GetValue<long>() ?? 0;
            url ??= (library["url"]?.GetValue<string>() ?? DefaultMaven).TrimEnd('/') + "/" + relativePath;
            result.Add(new LibraryArtifact(url, relativePath, sha1, size));
        }

        return result;
    }
}
