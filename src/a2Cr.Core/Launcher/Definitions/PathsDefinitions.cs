namespace a2Cr.Core.Launcher.Definitions;

public sealed class PathsDefinitions
{
    public PathsDefinitions(string? root = null)
    {
        RootDir = RootPath(root);
    }

    public string RootDir { get; }

    private string SharedDir => Path.Join(RootDir, "shared");
    public string AssetsDir => Path.Join(SharedDir, "assets");
    public string AssetsIndexesDir => Path.Join(AssetsDir, "indexes");
    public string AssetsObjectsDir => Path.Join(AssetsDir, "objects");
    public string LibrariesDir => Path.Join(SharedDir, "libraries");
    public string LogConfigsDir => Path.Join(SharedDir, "log_configs");
    public string VersionsDir => Path.Join(SharedDir, "versions");
    public string MetaDir => Path.Join(SharedDir, "meta");
    public string VersionManifestPath => Path.Join(MetaDir, "version_manifest_v2.json");

    private string PoolDir => Path.Join(RootDir, "pool");
    public string ModsPoolDir => Path.Join(PoolDir, "mods");
    public string ResourcePacksPoolDir => Path.Join(PoolDir, "resourcepacks");
    public string ShaderPacksPoolDir => Path.Join(PoolDir, "shaderpacks");
    public string OptionsPoolDir => Path.Join(PoolDir, "options");
    public string SavesPoolDir => Path.Join(PoolDir, "saves");

    public string InstancesDir => Path.Join(RootDir, "instances");
    public string ConfigsDir => Path.Join(RootDir, "configs");
    public string TemplatesDir => Path.Join(RootDir, "templates");
    public string LauncherLogsDir => Path.Join(RootDir, "logs", "launcher");
    public string TrashesDir => Path.Join(RootDir, ".trash");

    public static string RootPath(string? argRootPath = null)
    {
        if (argRootPath is { Length: > 0 }) return Path.GetFullPath(argRootPath);

        var environmentRoot = Environment.GetEnvironmentVariable("A2CR_ROOT");
        if (environmentRoot is { Length: > 0 }) return Path.GetFullPath(environmentRoot);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile)) throw new InvalidOperationException("没有找到用户目录。");

        return Path.Join(userProfile, ".a2CrLauncher");
    }

    public void CreateDirs()
    {
        Directory.CreateDirectory(AssetsDir);
        Directory.CreateDirectory(AssetsIndexesDir);
        Directory.CreateDirectory(AssetsObjectsDir);
        Directory.CreateDirectory(LibrariesDir);
        Directory.CreateDirectory(LogConfigsDir);
        Directory.CreateDirectory(VersionsDir);
        Directory.CreateDirectory(MetaDir);

        Directory.CreateDirectory(ModsPoolDir);
        Directory.CreateDirectory(ResourcePacksPoolDir);
        Directory.CreateDirectory(ShaderPacksPoolDir);
        Directory.CreateDirectory(OptionsPoolDir);
        Directory.CreateDirectory(SavesPoolDir);
        Directory.CreateDirectory(InstancesDir);
        Directory.CreateDirectory(ConfigsDir);
        Directory.CreateDirectory(TemplatesDir);
        Directory.CreateDirectory(LauncherLogsDir);
        Directory.CreateDirectory(TrashesDir);
    }
}