using System.Reflection;

namespace a2Cr.Core.Launcher.Definitions;

public static class LauncherDefinitions
{
    public const string LauncherName = "a2Cr-Launcher";

    private const string GithubRepositoryUrl = "https://github.com/CILXRY/a2Cr-Launcher";

    public const string AzureAppId = "0ef280c9-b0e1-4f9f-9729-faf39d825901";

    public static readonly string LauncherVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public static readonly string UserAgent =
        $"{LauncherName}/{LauncherVersion} ({GithubRepositoryUrl})";
}
