using System.Text.Json;
using a2Cr.Core.Errors;
using a2Cr.Core.Java;
using a2Cr.Core.Launch;
using a2Cr.Core.Meta;
using a2Cr.Core.Path;

namespace a2Cr.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return 0;
        }

        string? root = null;
        var json = args.Contains("--json");
        var refresh = args.Contains("--refresh");
        var positional = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--root" && index + 1 < args.Length)
            {
                root = args[++index];
            }
            else if (args[index] is "--json" or "--refresh" or "--no-progress")
            {
            }
            else if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                return Fail(json, "usage", $"未知选项 {args[index]}", "使用 --help 查看用法。", 2);
            }
            else
            {
                positional.Add(args[index]);
            }
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var paths = new LauncherPaths(root);
            paths.CreateDirs();
            if (positional.Count == 0) return Fail(json, "usage", "缺少命令。", "使用 --help 查看命令列表。", 2);
            var command = positional[0];
            switch (command)
            {
                case "version":
                {
                    var catalog = new VersionCatalog(paths);
                    var versions = await catalog.ListAsync(refresh, cancellation.Token);
                    if (json) WriteJson(new { ok = true, data = versions });
                    else foreach (var version in versions) Console.WriteLine($"{version.Id}\t{version.Type}");
                    return 0;
                }
                case "java" when positional.Count > 1 && positional[1] == "list":
                {
                    var runtimes = new JavaRuntimeFinder().Find();
                    if (json) WriteJson(new { ok = true, data = runtimes });
                    else foreach (var runtime in runtimes) Console.WriteLine($"Java {runtime.Major}\t{runtime.Version}\t{runtime.Home}");
                    return 0;
                }
                case "doctor":
                {
                    var report = await DoctorAsync(paths, cancellation.Token);
                    if (json) WriteJson(new { ok = true, data = report });
                    else PrintDoctor(report);
                    return 0;
                }
                case "install" when positional.Count > 1:
                {
                    var installer = new VersionInstaller(paths, new VersionCatalog(paths), new a2Cr.Core.Download.DownloadService());
                    await installer.InstallAsync(positional[1], refresh, null, cancellation.Token);
                    if (json) WriteJson(new { ok = true, data = new { version = positional[1] } });
                    else Console.WriteLine($"已安装版本 {positional[1]}");
                    return 0;
                }
                case "launch" when positional.Count > 1:
                {
                    var versionId = positional[1];
                    var catalog = new VersionCatalog(paths);
                    await catalog.GetAsync(versionId, refresh, cancellation.Token);
                    var meta = await new VersionMetaMerger(paths).ResolveAsync(versionId, cancellation.Token);
                    var gameDirectory = positional.Count > 2
                        ? positional[2]
                        : System.IO.Path.Combine(paths.InstancesDir, "default");
                    var plan = new LaunchArgumentBuilder(paths).Build(meta, versionId,
                        new LaunchOptions(gameDirectory));
                    var result = await new GameProcessLauncher().RunAsync(plan, gameDirectory,
                        new LaunchCallbacks(OnLog: line => Console.WriteLine(line)), cancellation.Token);
                    if (json) WriteJson(new { ok = true, data = new { version = versionId, exitCode = result.ExitCode } });
                    else Console.WriteLine($"游戏已退出，退出码：{result.ExitCode}");
                    return result.ExitCode == 0 ? 0 : 4;
                }
                default:
                    return Fail(json, "usage", $"未知命令 {command}。", "使用 --help 查看命令列表。", 2);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return Fail(json, "cancelled", "操作已取消。", null, 3);
        }
        catch (LauncherException exception)
        {
            return Fail(json, exception.Code, exception.Message, exception.Hint, 1);
        }
        catch (Exception exception)
        {
            return Fail(json, "internal", "启动器遇到未分类错误。", exception.Message, 1);
        }
    }

    private static async Task<object> DoctorAsync(LauncherPaths paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var java = new JavaRuntimeFinder().Find();
        var writable = true;
        try
        {
            var probe = Path.Combine(paths.RootDir, ".doctor-write-test");
            await File.WriteAllTextAsync(probe, "ok", cancellationToken);
            File.Delete(probe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            writable = false;
        }

        return new { root = paths.RootDir, writable, java, hardlinks = "未检测" };
    }

    private static void PrintDoctor(object report)
    {
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int Fail(bool json, string code, string message, string? hint, int exitCode)
    {
        if (json) WriteJson(new { ok = false, error = new { code, message, hint } });
        else Console.Error.WriteLine($"错误：{message}{(hint is null ? string.Empty : $"（建议：{hint}）")}");
        return exitCode;
    }

    private static void WriteJson(object value) => Console.WriteLine(JsonSerializer.Serialize(value));

    private static void PrintHelp() => Console.WriteLine(
        "a2Cr M1\n" +
        "用法：a2Cr <命令> [选项]\n\n" +
        "命令：\n" +
        "  version [--refresh]       列出版本\n" +
        "  install <version_id>      安装版本\n" +
        "  launch <version_id>      启动游戏\n" +
        "  java list                 列出 Java\n" +
        "  doctor                    检查环境\n\n" +
        "全局选项：--root <路径>  --json  --refresh  --no-progress");
}
