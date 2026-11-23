using System.Globalization;
using System.Text.Json.Nodes;
using a2Cr.Core.Errors;
using a2Cr.Core.Settings;
using a2Cr.Core.Storage;

namespace a2Cr.Core.Instances;

public enum InstanceLoadStatus
{
    Ok,
    Missing,
    Broken
}

public sealed record InstanceLoadResult(InstanceLoadStatus Status, InstanceMetadata? Metadata, string? Error);

public sealed class InstanceMetadata
{
    private readonly JsonObject _root;

    private InstanceMetadata(JsonObject root, string folder)
    {
        _root = root;
        Folder = folder;
    }

    public string Folder { get; }
    public int Format => GetInt("format") ?? 1;
    public string Name => GetString("name") ?? Folder;
    public string McVersion => GetString("mc_version") ?? string.Empty;
    public string VersionId => GetString("version_id") ?? string.Empty;
    public string LoaderType => GetString("loader", "type") ?? "vanilla";
    public string LoaderVersion => GetString("loader", "version") ?? string.Empty;
    public string? CreatedAt => GetString("created_at");
    public string? LastPlayed => GetString("last_played");
    public string? ClonedFrom => GetString("cloned_from");
    public string? Template => GetString("template");
    public JsonObject? Source => _root["source"] as JsonObject;
    public JsonObject? Overrides => _root["overrides"] as JsonObject;
    public JsonObject Root => _root;

    /// <summary>非法覆盖值回退全局，不阻止启动。</summary>
    public string? MaxMemoryOverride
    {
        get
        {
            var value = GetString("overrides", "max_memory");
            return MemoryValue.IsValid(value) ? MemoryValue.Normalize(value) : null;
        }
    }

    public string? JvmArgsOverride => GetString("overrides", "jvm_args");

    public int? JavaMajorOverride => GetInt("overrides", "java_major") is > 0 and var major ? major : null;

    public bool VerifyOnLaunch => GetBool("overrides", "verify_on_launch") ?? false;

    public static InstanceMetadata Create(string folder, string name, string mcVersion, string versionId,
        string loaderType = "vanilla", string loaderVersion = "", JsonObject? source = null,
        string? template = null, string? clonedFrom = null)
    {
        var root = new JsonObject
        {
            ["format"] = 1,
            ["name"] = name,
            ["mc_version"] = mcVersion,
            ["version_id"] = versionId,
            ["loader"] = new JsonObject { ["type"] = loaderType, ["version"] = loaderVersion },
            ["source"] = source?.DeepClone() ?? new JsonObject(),
            ["created_at"] = Now(),
            ["last_played"] = null,
            ["cloned_from"] = clonedFrom,
            ["template"] = template
        };
        return new InstanceMetadata(root, folder);
    }

    public static async Task<InstanceLoadResult> LoadAsync(string folder, string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new InstanceLoadResult(InstanceLoadStatus.Missing, null, null);
        }

        try
        {
            var root = await AtomicJson.ReadObjectAsync(path, cancellationToken).ConfigureAwait(false);
            if (root is null) return new InstanceLoadResult(InstanceLoadStatus.Missing, null, null);
            var format = root["format"]?.GetValue<int>() ?? 1;
            if (format > 1)
            {
                return new InstanceLoadResult(InstanceLoadStatus.Broken, null,
                    $"实例格式 {format} 高于当前支持的 1。");
            }

            root["format"] = 1;
            if (root["loader"] is not JsonObject)
            {
                root["loader"] = new JsonObject { ["type"] = "vanilla", ["version"] = "" };
            }

            if (root["source"] is not JsonObject) root["source"] = new JsonObject();
            if (root["name"] is null) root["name"] = folder;
            if (root["mc_version"] is null) root["mc_version"] = string.Empty;
            if (root["version_id"] is null) root["version_id"] = string.Empty;
            return new InstanceLoadResult(InstanceLoadStatus.Ok, new InstanceMetadata(root, folder), null);
        }
        catch (InstanceException exception)
        {
            return new InstanceLoadResult(InstanceLoadStatus.Broken, null, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new InstanceLoadResult(InstanceLoadStatus.Broken, null, exception.Message);
        }
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        await AtomicJson.WriteObjectAsync(path, _root, cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkPlayedAsync(string path, CancellationToken cancellationToken)
    {
        _root["last_played"] = Now();
        await SaveAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public JsonObject? GetOverrides() => _root["overrides"] as JsonObject;

    private string? GetString(params string[] keys)
    {
        try
        {
            return GetNode(keys)?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private int? GetInt(params string[] keys)
    {
        try
        {
            return GetNode(keys)?.GetValue<int>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private bool? GetBool(params string[] keys)
    {
        try
        {
            return GetNode(keys)?.GetValue<bool>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private JsonNode? GetNode(params string[] keys)
    {
        JsonNode? node = _root;
        foreach (var key in keys)
        {
            if (node is not JsonObject obj) return null;
            node = obj[key];
        }

        return node;
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
}
