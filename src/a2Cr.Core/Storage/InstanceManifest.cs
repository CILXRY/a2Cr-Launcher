using System.Text.Json.Nodes;
using a2Cr.Core.Errors;

namespace a2Cr.Core.Storage;

public enum FileOrigin
{
    Pack,
    Template,
    User,
    Pool
}

public static class FileOriginExtensions
{
    public static string ToContract(this FileOrigin origin) => origin switch
    {
        FileOrigin.Pack => "pack",
        FileOrigin.Template => "template",
        FileOrigin.Pool => "pool",
        _ => "user"
    };

    public static FileOrigin Parse(string? value) => value switch
    {
        "pack" => FileOrigin.Pack,
        "template" => FileOrigin.Template,
        "pool" => FileOrigin.Pool,
        _ => FileOrigin.User
    };
}

public sealed record ManifestEntry(
    string Path,
    string? Sha1,
    FileOrigin Origin,
    string? Sha1OnInstall,
    string? Template,
    string? Group);

public sealed class InstanceManifest
{
    private readonly JsonObject _root;
    private readonly JsonArray _files;

    private InstanceManifest(JsonObject root, JsonArray files)
    {
        _root = root;
        _files = files;
    }

    public IReadOnlyList<ManifestEntry> Entries => _files.OfType<JsonObject>()
        .Select(ToEntry)
        .Where(entry => entry is not null)
        .Cast<ManifestEntry>()
        .ToArray();

    public static async Task<InstanceManifest> LoadAsync(string path, CancellationToken cancellationToken)
    {
        JsonObject? root;
        try
        {
            root = await AtomicJson.ReadObjectAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (InstanceException)
        {
            throw;
        }

        if (root is null)
        {
            root = new JsonObject { ["format"] = 1, ["files"] = new JsonArray() };
        }

        var format = root["format"]?.GetValue<int>() ?? 1;
        if (format > 1)
        {
            throw new InstanceException("instance_broken", $"实例清单格式 {format} 高于当前支持的 1。",
                "请升级启动器，或修复 manifest.json。");
        }

        if (root["files"] is not JsonArray files)
        {
            files = new JsonArray();
            root["files"] = files;
        }

        root["format"] = 1;
        return new InstanceManifest(root, files);
    }

    public ManifestEntry? Find(string path)
    {
        var normalized = Normalize(path);
        var node = FindNode(normalized);
        return node is null ? null : ToEntry(node);
    }

    public void Upsert(string path, string? sha1, FileOrigin origin, string? sha1OnInstall = null,
        string? template = null, string? group = null)
    {
        var normalized = Normalize(path);
        var node = FindNode(normalized);
        if (node is null)
        {
            node = new JsonObject { ["path"] = normalized };
            _files.Add(node);
        }

        node["sha1"] = sha1;
        node["origin"] = origin.ToContract();
        if (sha1OnInstall is not null) node["sha1OnInstall"] = sha1OnInstall;
        if (template is not null) node["template"] = template;
        if (group is not null) node["group"] = group;
    }

    public bool Remove(string path)
    {
        var normalized = Normalize(path);
        var node = FindNode(normalized);
        if (node is null) return false;
        _files.Remove(node);
        return true;
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        await AtomicJson.WriteObjectAsync(path, _root, cancellationToken).ConfigureAwait(false);
    }

    public JsonObject Root => _root;

    private JsonObject? FindNode(string normalized) => _files.OfType<JsonObject>()
        .FirstOrDefault(node => string.Equals(Normalize(node["path"]?.GetValue<string>() ?? string.Empty),
            normalized, StringComparison.OrdinalIgnoreCase));

    private static ManifestEntry? ToEntry(JsonObject node)
    {
        var path = node["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path)) return null;
        return new ManifestEntry(Normalize(path), node["sha1"]?.GetValue<string>(),
            FileOriginExtensions.Parse(node["origin"]?.GetValue<string>()),
            node["sha1OnInstall"]?.GetValue<string>(), node["template"]?.GetValue<string>(),
            node["group"]?.GetValue<string>());
    }

    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
