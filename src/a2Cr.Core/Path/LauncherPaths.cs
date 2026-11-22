namespace a2Cr.Core.Path;

public sealed class LauncherPaths
{
    public LauncherPaths(string? root = null)
    {
        RootDir = RootPath(root);
    }

    public string RootDir { get; }

    public string SharedDir => System.IO.Path.Join(RootDir, "shared");
    public string AssetsDir => System.IO.Path.Join(SharedDir, "assets");
    public string AssetsIndexesDir => System.IO.Path.Join(AssetsDir, "indexes");
    public string AssetsObjectsDir => System.IO.Path.Join(AssetsDir, "objects");
    public string LibrariesDir => System.IO.Path.Join(SharedDir, "libraries");
    public string LogConfigsDir => System.IO.Path.Join(SharedDir, "log_configs");
    public string VersionsDir => System.IO.Path.Join(SharedDir, "versions");
    public string MetaDir => System.IO.Path.Join(SharedDir, "meta");
    public string VersionManifestPath => System.IO.Path.Join(MetaDir, "version_manifest_v2.json");

    public string PoolDir => System.IO.Path.Join(RootDir, "pool");
    public string ModsPoolDir => System.IO.Path.Join(PoolDir, "mods");
    public string ResourcePacksPoolDir => System.IO.Path.Join(PoolDir, "resourcepacks");
    public string ShaderPacksPoolDir => System.IO.Path.Join(PoolDir, "shaderpacks");
    public string OptionsPoolDir => System.IO.Path.Join(PoolDir, "options");
    public string SavesPoolDir => System.IO.Path.Join(PoolDir, "saves");

    public string InstancesDir => System.IO.Path.Join(RootDir, "instances");
    public string ConfigsDir => System.IO.Path.Join(RootDir, "configs");
    public string TemplatesDir => System.IO.Path.Join(RootDir, "templates");
    public string LauncherLogsDir => System.IO.Path.Join(RootDir, "logs", "launcher");
    public string TrashesDir => System.IO.Path.Join(RootDir, ".trash");

    public static string RootPath(string? argRootPath = null)
    {
        if (argRootPath is { Length: > 0 })
        {
            return System.IO.Path.GetFullPath(argRootPath);
        }

        var environmentRoot = Environment.GetEnvironmentVariable("A2CR_ROOT");
        if (environmentRoot is { Length: > 0 })
        {
            return System.IO.Path.GetFullPath(environmentRoot);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("没有找到用户目录。");
        }

        return System.IO.Path.Join(userProfile, ".a2CrLauncher");
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
