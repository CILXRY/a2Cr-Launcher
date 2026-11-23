namespace a2Cr.Core.Launcher;

public static class LauncherInfo
{
    public const string LauncherName = "a2Cr-Launcher";

    public static readonly string LauncherVersion =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public const string GithubRepositoryUrl = "https://github.com/CILXRY/a2Cr-Launcher";
}