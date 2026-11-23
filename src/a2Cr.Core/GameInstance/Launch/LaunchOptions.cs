namespace a2Cr.Core.GameInstance.Launch;

public sealed record LaunchOptions(
    string GameDirectory,
    string? JavaPath = null,
    string MaxMemory = "4G",
    string AccessToken = "0",
    string? PlayerUuid = null,
    string UserType = "legacy",
    string ClientId = "",
    string AuthXuid = "",
    IReadOnlyDictionary<string, bool>? Features = null,
    IReadOnlyDictionary<string, string>? FeatureVariables = null,
    string JvmArgs = "",
    int? JavaMajor = null,
    IReadOnlyDictionary<int, string>? JavaOverrides = null,
    IReadOnlyList<string>? JavaExtraPaths = null)
{
    public string PlayerName = Environment.UserName;
    public string LauncherName = Launcher.Definitions.LauncherDefinitions.LauncherName;
    public string LauncherVersion { get; init; } = Launcher.Definitions.LauncherDefinitions.LauncherVersion;
}