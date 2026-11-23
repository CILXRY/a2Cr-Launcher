using a2Cr.Core.Download;

namespace a2Cr.Core.Errors;

public class LauncherException : Exception
{
    public LauncherException(string code, string message, string? hint = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Hint = hint;
    }

    public string Code { get; }

    public string? Hint { get; }
}

public sealed class MetaIncompleteException : LauncherException
{
    public MetaIncompleteException(string message)
        : base("meta_incomplete", message)
    {
    }
}

public sealed class JavaNotFoundException : LauncherException
{
    public JavaNotFoundException(int requiredMajor)
        : base("java_missing", $"没有找到 Java {requiredMajor} 或更高版本。", $"请安装 Java {requiredMajor}，或把 JAVA_HOME 指向已有安装。")
    {
    }
}

public sealed class UsageException : LauncherException
{
    public UsageException(string message, string? hint = null)
        : base("usage", message, hint)
    {
    }
}

public sealed class InstanceException : LauncherException
{
    public InstanceException(string code, string message, string? hint = null, Exception? innerException = null)
        : base(code, message, hint, innerException)
    {
    }

    public static InstanceException Missing(string folder) =>
        new("instance_missing", $"实例 {folder} 不存在。", "使用 instance list 查看已有实例。");

    public static InstanceException Broken(string path) =>
        new("instance_broken", $"实例元数据损坏：{path}", "修复或删除该文件后重试；启动器不会覆盖原文件。");
}

public sealed class StorageException : LauncherException
{
    public StorageException(string message, string? hint = null, Exception? innerException = null)
        : base("storage_failed", message, hint, innerException)
    {
    }
}

public sealed class MissingFilesException : LauncherException
{
    public MissingFilesException(int missingCount, string hint)
        : base("missing_files", $"缺少 {missingCount} 个运行库或客户端文件，请先安装。", hint)
    {
    }
}

public sealed class DownloadException : LauncherException
{
    public DownloadException(IReadOnlyList<DownloadFailure> failures)
        : base("download_failed", $"有 {failures.Count} 个文件下载失败。", "检查网络或代理，重新执行可以继续已完成的下载。")
    {
        Failures = failures;
    }

    public IReadOnlyList<DownloadFailure> Failures { get; }
}
