using a2Cr.Core.Path;

namespace a2Cr.Cli;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--help" or "-h")
        {
            Console.WriteLine("a2Cr M1\n用法：a2Cr [--root <路径>] [--help]");
            return Task.FromResult(0);
        }

        string? root = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--root" && index + 1 < args.Length)
            {
                root = args[++index];
            }
            else if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"错误：未知选项 {args[index]}");
                return Task.FromResult(2);
            }
        }

        try
        {
            var paths = new LauncherPaths(root);
            paths.CreateDirs();
            Console.WriteLine($"数据目录：{paths.RootDir}");
            return Task.FromResult(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"错误：无法初始化数据目录（建议：检查目录权限）\n{exception.Message}");
            return Task.FromResult(1);
        }
    }
}
