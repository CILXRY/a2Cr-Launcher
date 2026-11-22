using System.Security.Cryptography;
using a2Cr.Core.Download;
using a2Cr.Core.Errors;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Storage;

public sealed record ContentSpec(string? Url, string? Sha1, long Size, string? LocalPath)
{
    public static ContentSpec Download(string url, string? sha1, long size) => new(url, sha1, size, null);
    public static ContentSpec Local(string existingFile) => new(null, null, new FileInfo(existingFile).Length, existingFile);
    public static ContentSpec Empty(int size = 0) => new(null, null, size, null);
}

public enum AcquireStatus
{
    Success,
    Skipped,
    Failed
}

public sealed record AcquireResult(AcquireStatus Status, string Path, string? Sha1, bool Copied = false);

public interface IFileStore
{
    Task<AcquireResult> AcquireAsync(string targetPath, ContentSpec spec, FileOrigin origin,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken);

    Task ReleaseAsync(string targetPath, CancellationToken cancellationToken);
}

public sealed class InstanceFileStore : IFileStore
{
    private readonly string _instanceDirectory;
    private readonly string _manifestPath;
    private readonly InstanceManifest _manifest;
    private readonly DownloadService _downloader;
    private readonly string? _templateId;
    private readonly string? _group;

    public InstanceFileStore(string instanceDirectory, InstanceManifest manifest, DownloadService downloader,
        string? templateId = null, string? group = null)
    {
        _instanceDirectory = IoPath.GetFullPath(instanceDirectory).TrimEnd(IoPath.DirectorySeparatorChar);
        _manifestPath = IoPath.Combine(_instanceDirectory, "manifest.json");
        _manifest = manifest;
        _downloader = downloader;
        _templateId = templateId;
        _group = group;
    }

    public async Task<AcquireResult> AcquireAsync(string targetPath, ContentSpec spec, FileOrigin origin,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var target = ValidateTarget(targetPath);
        var relative = Relative(target);
        var expectedSha1 = spec.Sha1;
        if (expectedSha1 is null && spec.LocalPath is not null)
        {
            expectedSha1 = await HashAsync(spec.LocalPath, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(target) && await MatchesAsync(target, spec.Size, expectedSha1, cancellationToken)
                .ConfigureAwait(false))
        {
            var hash = await HashAsync(target, cancellationToken).ConfigureAwait(false);
            UpdateManifest(relative, hash, origin);
            await _manifest.SaveAsync(_manifestPath, cancellationToken).ConfigureAwait(false);
            return new AcquireResult(AcquireStatus.Skipped, target, hash);
        }

        if (spec.Url is not null)
        {
            var report = await _downloader.DownloadAsync([
                new DownloadItem(spec.Url, target, spec.Sha1, spec.Size)
            ], progress: progress, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!report.AllSucceeded)
            {
                throw new DownloadException(report.Failed);
            }
        }
        else if (spec.LocalPath is not null)
        {
            if (IoPath.GetFullPath(spec.LocalPath).Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                return new AcquireResult(AcquireStatus.Skipped, target,
                    await HashAsync(target, cancellationToken).ConfigureAwait(false));
            }

            await AtomicJson.CopyAsync(spec.LocalPath, target, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await WriteEmptyAsync(target, spec.Size, cancellationToken).ConfigureAwait(false);
        }

        var finalHash = await HashAsync(target, cancellationToken).ConfigureAwait(false);
        if (expectedSha1 is not null && !string.Equals(finalHash, expectedSha1, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new StorageException($"文件哈希不匹配：{relative}", "重新获取该文件。");
        }

        UpdateManifest(relative, finalHash, origin);
        await _manifest.SaveAsync(_manifestPath, cancellationToken).ConfigureAwait(false);
        return new AcquireResult(AcquireStatus.Success, target, finalHash);
    }

    public async Task ReleaseAsync(string targetPath, CancellationToken cancellationToken)
    {
        var target = ValidateTarget(targetPath);
        if (File.Exists(target)) File.Delete(target);
        _manifest.Remove(Relative(target));
        await _manifest.SaveAsync(_manifestPath, cancellationToken).ConfigureAwait(false);
    }

    private void UpdateManifest(string relative, string hash, FileOrigin origin)
    {
        _manifest.Upsert(relative, hash, origin, hash, _templateId, _group);
    }

    private string ValidateTarget(string targetPath)
    {
        var target = IoPath.GetFullPath(targetPath);
        var root = _instanceDirectory + IoPath.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageException($"路径不在实例目录内：{targetPath}", "拒绝写入实例目录之外的位置。");
        }

        return target;
    }

    private string Relative(string target) =>
        IoPath.GetRelativePath(_instanceDirectory, target).Replace('\\', '/');

    private static async Task<bool> MatchesAsync(string path, long size, string? sha1,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (size > 0 && info.Length != size) return false;
        if (string.IsNullOrWhiteSpace(sha1)) return true;
        var hash = await HashAsync(path, cancellationToken).ConfigureAwait(false);
        return string.Equals(hash, sha1, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task WriteEmptyAsync(string target, long size, CancellationToken cancellationToken)
    {
        var temporary = target + ".tmp";
        Directory.CreateDirectory(IoPath.GetDirectoryName(target)!);
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                         4096, FileOptions.Asynchronous))
        {
            stream.SetLength(Math.Max(0, size));
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, target, true);
    }
}
