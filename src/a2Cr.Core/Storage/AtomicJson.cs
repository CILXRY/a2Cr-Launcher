using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using a2Cr.Core.Errors;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Storage;

public static class AtomicJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static async Task<JsonObject?> ReadObjectAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var offset = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        try
        {
            return JsonNode.Parse(Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset)) as JsonObject
                ?? throw new JsonException("根节点不是对象。");
        }
        catch (JsonException exception)
        {
            throw new InstanceException("instance_broken", $"JSON 文件损坏：{path}",
                "修复或删除该文件后重试；启动器不会覆盖原文件。", exception);
        }
    }

    public static async Task WriteObjectAsync(string path, JsonObject value, CancellationToken cancellationToken)
    {
        var directory = IoPath.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        try
        {
            var text = value.ToJsonString(Options);
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw new StorageException($"无法写入 {path}。", "检查磁盘空间和目录权限。", exception);
        }
    }

    public static async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        var directory = IoPath.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = target + ".tmp";
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                             1024 * 64, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, target, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw new StorageException($"无法复制到 {target}。", "检查磁盘空间和目录权限。", exception);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
