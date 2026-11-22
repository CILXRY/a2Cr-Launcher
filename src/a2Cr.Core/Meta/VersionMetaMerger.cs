using System.Text.Json;
using System.Text.Json.Nodes;
using a2Cr.Core.Errors;
using a2Cr.Core.Path;

namespace a2Cr.Core.Meta;

public sealed class VersionMetaMerger
{
    private readonly LauncherPaths _paths;

    public VersionMetaMerger(LauncherPaths paths)
    {
        _paths = paths;
    }

    public async Task<JsonObject> ResolveAsync(string versionId, CancellationToken cancellationToken)
    {
        return await ResolveAsync(versionId, [], cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonObject> ResolveAsync(
        string versionId,
        IReadOnlyList<string> stack,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stack.Contains(versionId, StringComparer.Ordinal))
        {
            var chain = string.Join(" -> ", stack.Append(versionId));
            throw new MetaIncompleteException($"版本继承出现循环：{chain}");
        }

        var meta = await ReadAsync(versionId, cancellationToken).ConfigureAwait(false);
        var parentId = meta["inheritsFrom"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(parentId))
        {
            meta["_baseVersion"] = meta["id"]?.GetValue<string>() ?? versionId;
            return meta;
        }

        var parent = await ResolveAsync(parentId, stack.Append(versionId).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        return Merge(parent, meta, parent["_baseVersion"]?.GetValue<string>() ?? parentId);
    }

    private async Task<JsonObject> ReadAsync(string versionId, CancellationToken cancellationToken)
    {
        var path = System.IO.Path.Combine(_paths.VersionsDir, versionId, $"{versionId}.json");
        if (!File.Exists(path))
        {
            throw new LauncherException("version_unknown", $"找不到版本 {versionId} 的元数据。", "请先安装这个版本。");
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (node is not JsonObject result)
            {
                throw new MetaIncompleteException($"版本 {versionId} 的元数据不是 JSON 对象。");
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new MetaIncompleteException($"版本 {versionId} 的元数据损坏。") { Source = exception.Source };
        }
    }

    private static JsonObject Merge(JsonObject parent, JsonObject child, string baseVersion)
    {
        var merged = (JsonObject)parent.DeepClone();
        merged["id"] = child["id"]?.DeepClone() ?? parent["id"]?.DeepClone();
        merged["type"] = child["type"]?.DeepClone() ?? parent["type"]?.DeepClone() ?? "release";
        merged["mainClass"] = child["mainClass"]?.DeepClone() ?? parent["mainClass"]?.DeepClone();
        merged["libraries"] = MergeLibraries(parent["libraries"] as JsonArray, child["libraries"] as JsonArray);
        merged["arguments"] = MergeArguments(parent["arguments"] as JsonObject, child["arguments"] as JsonObject);
        merged["_baseVersion"] = baseVersion;

        foreach (var property in new[] { "minecraftArguments", "logging", "javaVersion" })
        {
            if (child[property] is not null)
            {
                merged[property] = child[property]!.DeepClone();
            }
        }

        merged.Remove("inheritsFrom");
        return merged;
    }

    private static JsonArray MergeLibraries(JsonArray? parent, JsonArray? child)
    {
        var values = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var library in (parent ?? []).Concat(child ?? []))
        {
            if (library is not JsonObject obj)
            {
                continue;
            }

            var key = obj["name"]?.GetValue<string>()
                ?? obj["downloads"]?["artifact"]?["path"]?.GetValue<string>();
            if (key is not null)
            {
                values[key] = obj.DeepClone();
            }
        }

        var result = new JsonArray();
        foreach (var value in values.Values)
        {
            result.Add(value);
        }

        return result;
    }

    private static JsonObject MergeArguments(JsonObject? parent, JsonObject? child)
    {
        var result = new JsonObject();
        foreach (var key in (parent?.Select(property => property.Key) ?? []).Union(
                     child?.Select(property => property.Key) ?? [], StringComparer.Ordinal))
        {
            var values = new JsonArray();
            AddValues(values, parent?[key]);
            AddValues(values, child?[key]);
            result[key] = values;
        }

        return result;
    }

    private static void AddValues(JsonArray target, JsonNode? value)
    {
        if (value is not JsonArray array)
        {
            return;
        }

        foreach (var item in array)
        {
            target.Add(item?.DeepClone());
        }
    }
}
