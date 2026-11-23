using System.Globalization;
using System.Text.Json.Nodes;
using a2Cr.Core.Errors;
using a2Cr.Core.Launcher.Definitions;
using a2Cr.Core.Storage;

namespace a2Cr.Core.Settings;

public sealed class LauncherSettings
{
    private readonly JsonObject _root;

    private LauncherSettings(JsonObject root)
    {
        _root = root;
        EnsureDefaults();
    }

    public static async Task<LauncherSettings> LoadAsync(PathsDefinitions paths, CancellationToken cancellationToken)
    {
        var path = System.IO.Path.Combine(paths.ConfigsDir, "launcher.json");
        JsonObject? root;
        try
        {
            root = await AtomicJson.ReadObjectAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (InstanceException)
        {
            root = null;
        }

        var settings = new LauncherSettings(root ?? new JsonObject());
        await settings.SaveAsync(paths, cancellationToken).ConfigureAwait(false);
        return settings;
    }

    public string MaxMemory => MemoryValue.Normalize(GetString("launch", "max_memory"));

    public string JvmArgs => GetString("launch", "jvm_args") ?? string.Empty;

    public string InstanceIdStyle
    {
        get
        {
            var value = GetString("storage", "instance_id_style");
            return value is "slug" or "opaque" or "origin" ? value : "slug";
        }
    }

    public bool CloseAfterLaunch => GetBool("launch", "close_after_launch") ?? false;

    public int MaxConcurrency => Math.Clamp(GetInt("download", "max_concurrency") ?? 8, 1, 16);

    public int MaxRetry => Math.Clamp(GetInt("download", "max_retry") ?? 4, 0, 10);

    public string? MirrorBaseUrl
    {
        get
        {
            var value = GetString("download", "mirror_url");
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public string? ProxyUrl
    {
        get
        {
            if (GetString("download", "proxy", "mode") != "custom") return null;
            var value = GetString("download", "proxy", "url");
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public IReadOnlyDictionary<int, string> JavaOverrides
    {
        get
        {
            var result = new Dictionary<int, string>();
            if (GetObject("java")?["overrides"] is not JsonObject overrides) return result;
            foreach (var item in overrides)
            {
                if (int.TryParse(item.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)
                    && item.Value?.GetValue<string>() is { Length: > 0 } path)
                {
                    result[major] = path;
                }
            }

            return result;
        }
    }

    public IReadOnlyList<string> ExtraJavaPaths
    {
        get
        {
            if (GetObject("java")?["extra_paths"] is not JsonArray paths) return [];
            return paths.OfType<JsonValue>().Select(value => value.GetValue<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        }
    }

    public JsonObject Root => _root;

    public async Task SaveAsync(PathsDefinitions paths, CancellationToken cancellationToken)
    {
        var path = System.IO.Path.Combine(paths.ConfigsDir, "launcher.json");
        await AtomicJson.WriteObjectAsync(path, _root, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureDefaults()
    {
        _root["format"] = _root["format"]?.GetValue<int>() ?? 1;
        EnsureObject("launch",
            ("max_memory", "4G"), ("jvm_args", ""), ("new_instance_language", "inherit"),
            ("close_after_launch", false));
        var java = EnsureObject("java");
        if (java["overrides"] is not JsonObject) java["overrides"] = new JsonObject();
        if (java["extra_paths"] is not JsonArray) java["extra_paths"] = new JsonArray();
        EnsureObject("ui", ("theme", "system"), ("window_width", 1180), ("window_height", 760),
            ("animations", true), ("language", "zh-CN"));
        EnsureObject("advanced", ("log_level", "info"), ("log_keep_days", 7), ("keep_raw_command_line", false));
        EnsureObject("storage", ("saves_pool", false), ("dedup", false), ("instance_id_style", "slug"),
            ("trash_keep_days", 30));
        var download = EnsureObject("download", ("max_concurrency", 8), ("max_bytes_per_second", 0),
            ("mirror_url", ""), ("curseforge_api", "mirror"), ("curseforge_key", ""), ("max_retry", 4));
        if (download["proxy"] is not JsonObject)
        {
            download["proxy"] = new JsonObject { ["mode"] = "system", ["url"] = "" };
        }
    }

    private JsonObject EnsureObject(string name, params (string Key, object Value)[] defaults)
    {
        if (_root[name] is not JsonObject obj)
        {
            obj = new JsonObject();
            _root[name] = obj;
        }

        foreach (var (key, value) in defaults)
        {
            if (obj[key] is null) obj[key] = JsonValue.Create(value);
        }

        return obj;
    }

    private JsonObject? GetObject(params string[] keys)
    {
        JsonNode? node = _root;
        foreach (var key in keys)
        {
            if (node is not JsonObject obj) return null;
            node = obj[key];
        }

        return node as JsonObject;
    }

    private string? GetString(params string[] keys) => GetNode(keys)?.GetValue<string>();

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
}
