using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using a2Cr.Core.Download;
using a2Cr.Core.Errors;
using a2Cr.Core.Instances;
using a2Cr.Core.Java;
using a2Cr.Core.Launch;
using a2Cr.Core.Loaders;
using a2Cr.Core.Meta;
using a2Cr.Core.Path;
using a2Cr.Core.Settings;
using a2Cr.Core.Storage;
using IoPath = System.IO.Path;

namespace a2Cr.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // CLI 输出不是 HTML，中文直接写出来，脚本和人都好看。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static async Task<int> Main(string[] args)
    {
        // Windows 控制台默认可能用 GBK/OEM 代码页，中文帮助和错误会乱码。
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);
        }
        catch (IOException)
        {
            // 没有控制台（重定向/服务）时设置编码可能失败，不影响命令执行。
        }

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return 0;
        }

        var json = args.Contains("--json");
        CommandLine cli;
        try
        {
            cli = CommandLine.Parse(args);
        }
        catch (UsageException exception)
        {
            return Fail(json, exception, 2, false);
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var paths = new LauncherPaths(cli.Root);
        try
        {
            paths.CreateDirs();
            return await DispatchAsync(cli, paths, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return Fail(cli.Json, new LauncherException("cancelled", "操作已取消。"), 3, cli.Verbose);
        }
        catch (UsageException exception)
        {
            return Fail(cli.Json, exception, 2, cli.Verbose);
        }
        catch (LauncherException exception)
        {
            return Fail(cli.Json, exception, 1, cli.Verbose);
        }
        catch (Exception exception)
        {
            var wrapped = new LauncherException("internal", "启动器遇到未分类错误。", exception.Message, exception);
            return Fail(cli.Json, wrapped, 1, cli.Verbose, exception);
        }
    }

    private static async Task<int> DispatchAsync(CommandLine cli, LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        if (cli.Positionals.Count == 0)
        {
            throw new UsageException("缺少命令。", "使用 --help 查看命令列表。");
        }

        return cli.Positionals[0] switch
        {
            "version" => await VersionAsync(cli, paths, cancellationToken),
            "install" => await InstallAsync(cli, paths, cancellationToken),
            "launch" => await LaunchAsync(cli, paths, cancellationToken),
            "java" => Java(cli),
            "doctor" => await DoctorAsync(cli, paths, cancellationToken),
            "instance" => await InstanceAsync(cli, paths, cancellationToken),
            _ => throw new UsageException($"未知命令 {cli.Positionals[0]}。", "使用 --help 查看命令列表。")
        };
    }

    private static async Task<int> VersionAsync(CommandLine cli, LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        var type = (cli.Get("type") ?? "release").ToLowerInvariant();
        if (type is not ("release" or "snapshot" or "all"))
        {
            throw new UsageException("--type 只能是 release、snapshot 或 all。");
        }

        var limit = cli.GetInt("limit", 20, 1, 500);
        var versions = await new VersionCatalog(paths).ListAsync(cli.Has("refresh"), cancellationToken)
            .ConfigureAwait(false);
        var selected = versions.Where(version => type == "all" || version.Type == type)
            .Take(limit)
            .Select(version => new
            {
                id = version.Id,
                type = version.Type,
                releaseTime = version.ReleaseTime
            })
            .ToArray();

        if (cli.Json) WriteJson(new { ok = true, data = selected });
        else foreach (var version in selected)
        {
            Console.WriteLine($"{version.id}\t{version.type}\t{version.releaseTime?.ToString("yyyy-MM-dd") ?? "-"}");
        }

        return 0;
    }

    private static async Task<int> InstallAsync(CommandLine cli, LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        if (cli.Positionals.Count < 2)
        {
            throw new UsageException("install 需要版本 id。",
                "用法：a2Cr install <version_id> [--loader fabric|quilt] [--loader-version V]");
        }

        var versionId = cli.Positionals[1];
        var progress = CreateProgress(cli);
        using var http = new HttpClient();
        var downloader = new DownloadService(http);
        var catalog = new VersionCatalog(paths, http, downloader);
        var versionInstaller = new VersionInstaller(paths, catalog, downloader);
        var loaderText = cli.Get("loader");
        if (loaderText is null)
        {
            if (cli.Get("loader-version") is not null)
            {
                throw new UsageException("--loader-version 需要和 --loader 一起使用。",
                    "例如：a2Cr install 1.21.1 --loader fabric --loader-version 0.16.9");
            }

            await versionInstaller.InstallAsync(versionId, cli.Has("refresh"), progress, cancellationToken)
                .ConfigureAwait(false);
            if (cli.Json) WriteJson(new { ok = true, data = new { version = versionId } });
            else Console.WriteLine($"已安装版本 {versionId}");
            return 0;
        }

        if (!LoaderKinds.TryParse(loaderText, out var loader))
        {
            throw new UsageException($"未知加载器 {loaderText}。",
                "M2 支持 fabric 和 quilt；forge / neoforge 将在后续里程碑提供。");
        }

        if (loader == LoaderKind.Vanilla)
        {
            throw new UsageException("--loader vanilla 不需要指定。", "直接运行 install <version_id> 即可。");
        }

        var installer = new LoaderInstaller(paths, http, downloader, versionInstaller);
        var result = await installer.InstallAsync(versionId, loader, cli.Get("loader-version"), progress,
            cancellationToken).ConfigureAwait(false);
        if (cli.Json)
        {
            WriteJson(new
            {
                ok = true,
                data = new
                {
                    versionId = result.VersionId,
                    minecraft = result.MinecraftVersion,
                    loader = loader.ToContract(),
                    loaderVersion = result.LoaderVersion,
                    libraries = result.LibraryCount
                }
            });
        }
        else
        {
            Console.WriteLine($"已安装 {loader.ToContract()} {result.LoaderVersion}（版本 id：{result.VersionId}）");
        }

        return 0;
    }

    private static async Task<int> LaunchAsync(CommandLine cli, LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        var versionId = cli.Get("version");
        var instance = cli.Get("instance");
        if ((versionId is null) == (instance is null))
        {
            throw new UsageException("launch 必须且只能指定 --version 或 --instance。",
                "例如：a2Cr launch --version 1.21.1 或 a2Cr launch --instance mechanical-pack");
        }

        if (cli.Get("account") is not null)
        {
            throw new UsageException("账号功能将在 M4 提供。", "当前版本使用离线启动，可以去掉 --account。");
        }

        var memory = cli.Get("memory");
        if (memory is not null && !MemoryValue.IsValid(memory))
        {
            throw new UsageException($"内存值 {memory} 不合法。", "使用 4G / 512M 这样的格式。");
        }

        var javaMajor = cli.GetOptionalInt("java-major", 1, 99);
        var playerName = cli.Get("username") ?? "a2Cr";
        var detach = cli.Has("detach");
        var onLog = detach ? null : new Action<string>(line => Console.Error.WriteLine(line));
        var overrides = new LaunchOverrides(memory, null, javaMajor, playerName);
        var settings = await LauncherSettings.LoadAsync(paths, cancellationToken).ConfigureAwait(false);

        LaunchResult result;
        string target;
        if (instance is not null)
        {
            var service = new InstanceLaunchService(paths, settings);
            result = await service.LaunchAsync(instance, overrides, onLog, cancellationToken, detach)
                .ConfigureAwait(false);
            target = instance;
        }
        else
        {
            var catalog = new VersionCatalog(paths);
            await catalog.GetAsync(versionId!, false, cancellationToken).ConfigureAwait(false);
            var meta = await new VersionMetaMerger(paths).ResolveAsync(versionId!, cancellationToken)
                .ConfigureAwait(false);
            var gameDirectory = IoPath.Combine(paths.InstancesDir, versionId!);
            var plan = new LaunchArgumentBuilder(paths).Build(meta, versionId!, new LaunchOptions(
                gameDirectory,
                PlayerName: playerName,
                MaxMemory: memory ?? settings.MaxMemory,
                JvmArgs: settings.JvmArgs,
                JavaMajor: javaMajor,
                JavaOverrides: settings.JavaOverrides,
                JavaExtraPaths: settings.ExtraJavaPaths));
            result = await new GameProcessLauncher().RunAsync(plan, gameDirectory,
                new LaunchCallbacks(OnLog: onLog), cancellationToken, detach).ConfigureAwait(false);
            target = versionId!;
        }

        if (cli.Json)
        {
            WriteJson(new
            {
                ok = true,
                data = new { target, exitCode = result.ExitCode, detached = detach }
            });
        }
        else if (detach)
        {
            Console.WriteLine($"已在后台启动 {target}");
        }
        else
        {
            Console.WriteLine($"游戏已退出，退出码：{result.ExitCode}");
        }

        return detach || result.ExitCode == 0 ? 0 : 1;
    }

    private static int Java(CommandLine cli)
    {
        if (cli.Positionals.Count < 2 || cli.Positionals[1] != "list")
        {
            throw new UsageException("java 需要子命令 list。", "用法：a2Cr java list");
        }

        var runtimes = new JavaRuntimeFinder().Find()
            .Select(runtime => new { major = runtime.Major, version = runtime.Version, home = runtime.Home })
            .ToArray();
        if (cli.Json) WriteJson(new { ok = true, data = runtimes });
        else if (runtimes.Length == 0) Console.WriteLine("没有发现 Java。");
        else foreach (var runtime in runtimes)
        {
            Console.WriteLine($"Java {runtime.major}\t{runtime.version}\t{runtime.home}");
        }

        return 0;
    }

    private static async Task<int> DoctorAsync(CommandLine cli, LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        var report = await BuildDoctorReportAsync(paths, cancellationToken).ConfigureAwait(false);
        if (cli.Json) WriteJson(new { ok = true, data = report });
        else PrintDoctor(report);
        return 0;
    }

    private static async Task<DoctorReport> BuildDoctorReportAsync(LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var writable = true;
        var probe = IoPath.Combine(paths.RootDir, ".doctor-write-test");
        try
        {
            await File.WriteAllTextAsync(probe, "ok", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            writable = false;
        }
        finally
        {
            TryDelete(probe);
        }

        var hardLinks = false;
        var linkSource = IoPath.Combine(paths.RootDir, ".doctor-link-source");
        var linkTarget = IoPath.Combine(paths.RootDir, ".doctor-link-target");
        try
        {
            await File.WriteAllTextAsync(linkSource, "ok", cancellationToken).ConfigureAwait(false);
            hardLinks = HardLink.TryCreate(linkSource, linkTarget);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            hardLinks = false;
        }
        finally
        {
            TryDelete(linkSource);
            TryDelete(linkTarget);
        }

        var freeBytes = 0L;
        try
        {
            var root = IoPath.GetPathRoot(paths.RootDir);
            if (!string.IsNullOrWhiteSpace(root)) freeBytes = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            freeBytes = 0;
        }

        var java = new JavaRuntimeFinder().Find()
            .Select(runtime => new DoctorJava(runtime.Major, runtime.Version, runtime.Home))
            .ToArray();
        var manifestTime = File.Exists(paths.VersionManifestPath)
            ? File.GetLastWriteTime(paths.VersionManifestPath)
            : (DateTime?)null;
        return new DoctorReport(paths.RootDir, writable, hardLinks, freeBytes, java, manifestTime);
    }

    private static void PrintDoctor(DoctorReport report)
    {
        Console.WriteLine($"数据目录：{report.Root}");
        Console.WriteLine($"可写：{(report.Writable ? "是" : "否")}");
        Console.WriteLine($"硬链接：{(report.HardLinks ? "可用" : "不可用（会退化为复制）")}");
        Console.WriteLine($"剩余空间：{FormatBytes(report.FreeBytes)}");
        if (report.Java.Count == 0) Console.WriteLine("Java：没有发现");
        else foreach (var java in report.Java)
        {
            Console.WriteLine($"Java {java.Major}\t{java.Version}\t{java.Home}");
        }

        Console.WriteLine($"版本清单缓存：{(report.VersionManifestTime is { } time ? time.ToString("yyyy-MM-dd HH:mm") : "没有")}");
    }

    private static async Task<int> InstanceAsync(CommandLine cli, LauncherPaths paths,
        CancellationToken cancellationToken)
    {
        if (cli.Positionals.Count < 2)
        {
            throw new UsageException("instance 需要子命令。", "可用：list、create、clone、remove、open");
        }

        var settings = await LauncherSettings.LoadAsync(paths, cancellationToken).ConfigureAwait(false);
        var manager = new InstanceManager(paths, settings);
        switch (cli.Positionals[1])
        {
            case "list":
            {
                var list = await manager.ListAsync(cancellationToken).ConfigureAwait(false);
                if (cli.Json)
                {
                    WriteJson(new
                    {
                        ok = true,
                        data = list.Select(item => new
                        {
                            folder = item.Folder,
                            name = item.Name,
                            mcVersion = item.McVersion,
                            versionId = item.VersionId,
                            loaderType = item.LoaderType,
                            loaderVersion = item.LoaderVersion,
                            lastPlayed = item.LastPlayed,
                            broken = item.Broken,
                            error = item.Error
                        })
                    });
                }
                else if (list.Count == 0)
                {
                    Console.WriteLine("还没有实例。用 a2Cr instance create <名字> --version <版本> 创建一个。");
                }
                else
                {
                    foreach (var item in list)
                    {
                        var loader = item.LoaderType == "vanilla" || item.LoaderType.Length == 0
                            ? "vanilla"
                            : $"{item.LoaderType} {item.LoaderVersion}";
                        Console.WriteLine(
                            $"{item.Folder}\t{item.Name}\t{item.McVersion}\t{loader}\t{item.LastPlayed ?? "-"}{(item.Broken ? "\t[损坏]" : string.Empty)}");
                    }
                }

                return 0;
            }
            case "create":
            {
                if (cli.Positionals.Count < 3)
                {
                    throw new UsageException("instance create 需要实例名。",
                        "用法：a2Cr instance create <名字> --version <版本 id>");
                }

                var versionId = cli.Get("version")
                                ?? throw new UsageException("instance create 需要 --version。",
                                    "用法：a2Cr instance create <名字> --version <版本 id>");
                var identity = await LoaderIdentity.ReadAsync(paths, versionId, cancellationToken)
                    .ConfigureAwait(false);
                var result = await manager.CreateAsync(cli.Positionals[2], identity.MinecraftVersion, versionId,
                    identity.LoaderType, identity.LoaderVersion, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (cli.Json)
                {
                    WriteJson(new
                    {
                        ok = true,
                        data = new
                        {
                            folder = result.Metadata.Folder,
                            name = result.Metadata.Name,
                            mcVersion = result.Metadata.McVersion,
                            versionId = result.Metadata.VersionId,
                            loaderType = result.Metadata.LoaderType,
                            loaderVersion = result.Metadata.LoaderVersion,
                            reconcile = result.Reconcile
                        }
                    });
                }
                else
                {
                    Console.WriteLine(
                        $"已创建实例 {result.Metadata.Folder}（链接 {result.Reconcile.Linked}，复制 {result.Reconcile.Copied}，移除 {result.Reconcile.Removed}）");
                }

                return 0;
            }
            case "clone":
            {
                if (cli.Positionals.Count < 4)
                {
                    throw new UsageException("instance clone 需要来源目录和新名字。",
                        "用法：a2Cr instance clone <来源目录> <新名字> [--copy]");
                }

                var result = await manager.CloneAsync(cli.Positionals[2], cli.Positionals[3],
                    cli.Has("copy"), cancellationToken).ConfigureAwait(false);
                if (cli.Json)
                {
                    WriteJson(new
                    {
                        ok = true,
                        data = new
                        {
                            folder = result.Metadata.Folder,
                            name = result.Metadata.Name,
                            clonedFrom = result.Metadata.ClonedFrom,
                            reconcile = result.Reconcile
                        }
                    });
                }
                else
                {
                    Console.WriteLine(
                        $"已克隆实例 {result.Metadata.Folder}（链接 {result.Reconcile.Linked}，复制 {result.Reconcile.Copied}）");
                }

                return 0;
            }
            case "remove":
            {
                if (cli.Positionals.Count < 3)
                {
                    throw new UsageException("instance remove 需要实例目录。",
                        "用法：a2Cr instance remove <目录>");
                }

                var trash = await manager.RemoveAsync(cli.Positionals[2], cancellationToken).ConfigureAwait(false);
                if (cli.Json) WriteJson(new { ok = true, data = new { folder = cli.Positionals[2], trash } });
                else Console.WriteLine($"已移到回收站：{trash}");
                return 0;
            }
            case "open":
            {
                if (cli.Positionals.Count < 3)
                {
                    throw new UsageException("instance open 需要实例目录。", "用法：a2Cr instance open <目录>");
                }

                var directory = manager.InstanceDirectory(cli.Positionals[2]);
                if (!Directory.Exists(directory)) throw InstanceException.Missing(cli.Positionals[2]);
                OpenFolder(directory);
                if (cli.Json) WriteJson(new { ok = true, data = new { folder = cli.Positionals[2], path = directory } });
                else Console.WriteLine($"已打开：{directory}");
                return 0;
            }
            default:
                throw new UsageException($"未知的 instance 子命令 {cli.Positionals[1]}。",
                    "可用：list、create、clone、remove、open");
        }
    }

    private static void OpenFolder(string directory)
    {
        try
        {
            var startInfo = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe") { UseShellExecute = true }
                : OperatingSystem.IsMacOS()
                    ? new ProcessStartInfo("open") { UseShellExecute = false }
                    : new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            startInfo.ArgumentList.Add(directory);
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new LauncherException("open_failed", $"无法打开目录：{directory}", "手动打开该目录。", exception);
        }
    }

    private static IProgress<DownloadProgress> CreateProgress(CommandLine cli) =>
        new ConsoleProgress(!cli.Has("no-progress") && !Console.IsErrorRedirected);

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

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "未知";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.#} {units[index]}";
    }

    private static int Fail(bool json, LauncherException exception, int exitCode, bool verbose,
        Exception? raw = null)
    {
        if (json)
        {
            WriteJson(new
            {
                ok = false,
                error = new { code = exception.Code, message = exception.Message, hint = exception.Hint }
            });
        }
        else
        {
            var hint = exception.Hint is null ? string.Empty : $"（建议：{exception.Hint}）";
            Console.Error.WriteLine($"错误：{exception.Message}{hint}");
        }

        if (verbose) Console.Error.WriteLine((raw ?? exception).ToString());
        return exitCode;
    }

    private static void WriteJson(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static void PrintHelp() => Console.WriteLine(
        "a2Cr M2\n" +
        "用法：a2Cr <命令> [选项]\n\n" +
        "命令：\n" +
        "  version [--type release|snapshot|all] [--limit N] [--refresh]   列出版本\n" +
        "  install <version_id> [--loader fabric|quilt] [--loader-version V] 安装原版或加载器\n" +
        "  launch --version <id> | --instance <目录> [--memory 4G] [--java-major 21]\n" +
        "         [--offline] [--detach] [--username 名字]                  启动游戏\n" +
        "  java list                                                      列出 Java\n" +
        "  doctor                                                         检查环境\n" +
        "  instance list                                                  列出实例\n" +
        "  instance create <名字> --version <版本 id>                      创建实例\n" +
        "  instance clone <目录> <新名字> [--copy]                         克隆实例\n" +
        "  instance remove <目录>                                         移到回收站\n" +
        "  instance open <目录>                                           打开实例目录\n\n" +
        "全局选项：--root <路径>  --json  --verbose  --yes  --no-progress");

    private sealed record DoctorReport(
        string Root,
        bool Writable,
        bool HardLinks,
        long FreeBytes,
        IReadOnlyList<DoctorJava> Java,
        DateTime? VersionManifestTime);

    private sealed record DoctorJava(int Major, string Version, string Home);

    private sealed class ConsoleProgress(bool enabled) : IProgress<DownloadProgress>
    {
        private int _lastPercent = -1;

        public void Report(DownloadProgress value)
        {
            if (!enabled) return;
            var percent = value.TotalBytes > 0
                ? (int)(value.DoneBytes * 100 / value.TotalBytes)
                : value.TotalFiles > 0
                    ? value.DoneFiles * 100 / value.TotalFiles
                    : 0;
            if (percent == _lastPercent) return;
            _lastPercent = percent;
            Console.Error.Write($"\r下载 {value.DoneFiles}/{value.TotalFiles} 个文件 {percent}%");
            if (value.DoneFiles >= value.TotalFiles) Console.Error.WriteLine();
        }
    }
}
