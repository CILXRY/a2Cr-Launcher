using System.Text;
using System.Text.Json.Nodes;
using a2Cr.Core.Errors;
using a2Cr.Core.Java;
using a2Cr.Core.Launcher.Definitions;
using a2Cr.Core.Meta;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Launch;

public sealed record LaunchOptions(
    string GameDirectory,
    string PlayerName = "a2Cr",
    string? JavaPath = null,
    string MaxMemory = "4G",
    string AccessToken = "0",
    string? PlayerUuid = null,
    string UserType = "legacy",
    string ClientId = "",
    string AuthXuid = "",
    IReadOnlyDictionary<string, bool>? Features = null,
    IReadOnlyDictionary<string, string>? FeatureVariables = null,
    string LauncherName = "a2cr-launcher",
    string LauncherVersion = "0.5.0-beta",
    string JvmArgs = "",
    int? JavaMajor = null,
    IReadOnlyDictionary<int, string>? JavaOverrides = null,
    IReadOnlyList<string>? JavaExtraPaths = null);

public sealed record LaunchPlan(
    string JavaExecutable,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> Classpath,
    string NativesDirectory,
    string BaseVersion);

public static class ClasspathBuilder
{
    public static IReadOnlyList<string> Build(JsonObject meta, PathsDefinitions paths, string versionId,
        string osName, string osArch, bool includeClient)
    {
        var result = new List<string>();
        foreach (var library in LibraryResolver.BuildDownloads(meta, paths, osName, osArch))
        {
            result.Add(IoPath.Combine(paths.LibrariesDir,
                library.RelativePath.Replace('/', IoPath.DirectorySeparatorChar)));
        }

        if (includeClient)
        {
            var baseVersion = meta["_baseVersion"]?.GetValue<string>() ?? versionId;
            result.Add(IoPath.Combine(paths.VersionsDir, baseVersion, "client.jar"));
        }

        return result;
    }

    public static void EnsureFilesExist(IEnumerable<string> files)
    {
        var missing = files.Where(file => !File.Exists(file)).ToArray();
        if (missing.Length > 0)
        {
            throw new LauncherException("install_incomplete", $"缺少 {missing.Length} 个运行库或客户端文件，请先安装。",
                string.Join(Environment.NewLine, missing.Take(12)));
        }
    }
}

public sealed class LaunchArgumentBuilder
{
    private readonly PathsDefinitions _paths;
    private readonly JavaRuntimeFinder _javaFinder;

    public LaunchArgumentBuilder(PathsDefinitions paths, JavaRuntimeFinder? javaFinder = null)
    {
        _paths = paths;
        _javaFinder = javaFinder ?? new JavaRuntimeFinder();
    }

    public LaunchPlan Build(JsonObject meta, string versionId, LaunchOptions options)
    {
        var arguments = meta["arguments"] as JsonObject
                        ?? throw new MetaIncompleteException("版本信息不完整：缺少现代 arguments 字段。");
        if (meta["mainClass"]?.GetValue<string>() is not { Length: > 0 } mainClass)
        {
            throw new MetaIncompleteException("版本信息不完整：缺少 mainClass。");
        }

        var requiredJava = meta["javaVersion"]?["majorVersion"]?.GetValue<int>() ?? 8;
        if (options.JavaMajor is int requestedJava && requestedJava < requiredJava)
        {
            throw new LauncherException("java_invalid",
                $"这个版本要求 Java {requiredJava} 或更高，不能使用 Java {requestedJava}。",
                $"改用 Java {requiredJava} 或更高版本。");
        }

        var java = ResolveJava(options, requiredJava);

        var baseVersion = meta["_baseVersion"]?.GetValue<string>() ?? versionId;
        var mainClassUsesBootstrap = mainClass.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase);
        var classpath = ClasspathBuilder.Build(meta, _paths, versionId,
            Core.Launcher.Platform.SystemInfo.PlatformName, Core.Launcher.Platform.SystemInfo.PlatformArchitecture,
            !mainClassUsesBootstrap);
        ClasspathBuilder.EnsureFilesExist(classpath);

        var natives = IoPath.Combine(_paths.VersionsDir, versionId, "natives");
        Directory.CreateDirectory(natives);
        var values = BuildValues(meta, versionId, baseVersion, options, classpath, natives);
        var result = new List<string> { java, $"-Xmx{options.MaxMemory}", $"-Xms{options.MaxMemory}" };
        if (!string.IsNullOrWhiteSpace(options.JvmArgs)) result.AddRange(SplitArguments(options.JvmArgs));
        AppendArguments(result, arguments["jvm"], meta, values, options.Features);
        AppendLogging(result, meta, values);
        result.Add(mainClass);
        AppendArguments(result, arguments["game"], meta, values, options.Features);
        return new LaunchPlan(java, result.Skip(1).ToArray(), classpath, natives, baseVersion);
    }

    private string ResolveJava(LaunchOptions options, int requiredJava)
    {
        if (options.JavaPath is not null)
        {
            return JavaRuntimeFinder.ResolveExecutable(options.JavaPath)
                   ?? throw new LauncherException("java_invalid", $"设置中的 Java 路径无效：{options.JavaPath}");
        }

        var requestedMajor = options.JavaMajor ?? requiredJava;
        if (options.JavaOverrides?.TryGetValue(requestedMajor, out var configuredPath) == true)
        {
            return JavaRuntimeFinder.ResolveExecutable(configuredPath)
                   ?? throw new LauncherException("java_invalid", $"设置中的 Java 路径无效：{configuredPath}");
        }

        return _javaFinder.Select(requestedMajor, options.JavaExtraPaths).ExecutablePath;
    }

    /// <summary>
    /// 把 <c>launch.jvm_args</c> 按空白拆成参数。
    /// 契约规定不解析引号——需要引号的参数请写参数文件，避免各平台转义规则打架。
    /// </summary>
    public static IReadOnlyList<string> SplitArguments(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private Dictionary<string, string> BuildValues(JsonObject meta, string versionId, string baseVersion,
        LaunchOptions options, IReadOnlyList<string> classpath, string natives)
    {
        var playerUuid = options.PlayerUuid ?? GameInstance.Auth.Offline.GenerateOfflineUuid(options.PlayerName);
        var assetIndex = meta["assetIndex"]?["id"]?.GetValue<string>()
                         ?? throw new MetaIncompleteException("版本信息不完整：缺少 assetIndex.id。");
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = options.PlayerName,
            ["version_name"] = versionId,
            ["game_directory"] = IoPath.GetFullPath(options.GameDirectory),
            ["assets_root"] = _paths.AssetsDir,
            ["assets_index_name"] = assetIndex,
            ["auth_uuid"] = playerUuid,
            ["auth_access_token"] = options.AccessToken,
            ["user_type"] = options.UserType,
            ["clientid"] = options.ClientId,
            ["auth_xuid"] = options.AuthXuid,
            ["natives_directory"] = natives,
            ["library_directory"] = _paths.LibrariesDir,
            ["classpath"] = string.Join(IoPath.PathSeparator, classpath),
            ["classpath_separator"] = IoPath.PathSeparator.ToString(),
            ["launcher_name"] = options.LauncherName,
            ["launcher_version"] = options.LauncherVersion,
            ["version_type"] = meta["type"]?.GetValue<string>() ?? "release",
            ["max_memory"] = options.MaxMemory
        };
    }

    private static void AppendArguments(List<string> target, JsonNode? raw, JsonObject meta,
        IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, bool>? features)
    {
        if (raw is not JsonArray arguments) return;
        foreach (var item in arguments)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var text))
            {
                target.Add(Substitute(text, values));
                continue;
            }

            if (item is not JsonObject conditional) continue;
            if (!RuleEvaluator.IsAllowed(conditional["rules"], Launcher.Platform.SystemInfo.PlatformName,
                    Launcher.Platform.SystemInfo.PlatformArchitecture, features)) continue;
            AddValue(target, conditional["value"], values);
        }
    }

    private void AppendLogging(List<string> target, JsonObject meta, IReadOnlyDictionary<string, string> values)
    {
        if (meta["logging"]?["client"] is not JsonObject client) return;
        var argument = client["argument"]?.GetValue<string>();
        var fileId = client["file"]?["id"]?.GetValue<string>();
        if (argument is null || fileId is null) throw new MetaIncompleteException("版本信息不完整：logging.client 缺少字段。");
        var loggingValues = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            ["path"] = IoPath.Combine(_paths.LogConfigsDir, fileId)
        };
        target.Add(Substitute(argument, loggingValues));
    }

    private static void AddValue(List<string> target, JsonNode? value, IReadOnlyDictionary<string, string> values)
    {
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text))
        {
            target.Add(Substitute(text, values));
            return;
        }

        if (value is JsonArray array)
        {
            foreach (var item in array) AddValue(target, item, values);
        }
    }

    private static string Substitute(string text, IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (index + 2 < text.Length && text[index..].StartsWith("${", StringComparison.Ordinal))
            {
                var end = text.IndexOf('}', index + 2);
                if (end < 0) throw new MetaIncompleteException($"版本参数占位符未闭合：{text}");
                var key = text[(index + 2)..end];
                if (!values.TryGetValue(key, out var replacement))
                    throw new MetaIncompleteException($"版本参数包含未知占位符：{key}");
                builder.Append(replacement);
                index = end;
            }
            else builder.Append(text[index]);
        }

        return builder.ToString();
    }
}