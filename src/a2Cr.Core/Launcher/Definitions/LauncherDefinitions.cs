namespace a2Cr.Core.Launcher.Definitions;

public static class LauncherDefinitions
{
    public const string LauncherName = "a2Cr-Launcher";

    public static readonly string LauncherVersion =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    private const string GithubRepositoryUrl = "https://github.com/CILXRY/a2Cr-Launcher";

    public static readonly string UserAgent =
        $"{LauncherName}/{LauncherVersion} ({GithubRepositoryUrl})";
}