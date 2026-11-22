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
