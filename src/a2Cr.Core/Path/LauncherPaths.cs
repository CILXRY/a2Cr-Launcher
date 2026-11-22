namespace a2Cr.Core.Path;

public class LauncherPaths
{
    // 根目录
    public static string RootDir { get; } = RootPath(@"C:\Users\Rxx\Desktop\a2Cr");

    // 共享目录
    private static string SharedDir { get; } = System.IO.Path.Join(RootDir, "shared");
    public static string AssetsDir { get; } = System.IO.Path.Join(SharedDir, "assets");
    public static string LibrariesDir { get; } = System.IO.Path.Join(SharedDir, "libraries");
    public static string LogConfigsDir { get; } = System.IO.Path.Join(SharedDir, "log_configs");
    public static string VersionsDir { get; } = System.IO.Path.Join(SharedDir, "versions");
    public static string MetasDir { get; } = System.IO.Path.Join(SharedDir, "metas");

    // 资源池
    private static string PoolDir { get; } = System.IO.Path.Join(RootDir, "pool");
    public static string ModsPoolDir { get; } = System.IO.Path.Join(PoolDir, "mods");

    // 单项文件夹
    public static string InstancesDir { get; } = System.IO.Path.Join(RootDir, "instances");
    public static string ConfigsDir { get; } = System.IO.Path.Join(RootDir, "configs");
    public static string TemplatesDir { get; } = System.IO.Path.Join(RootDir, "templates");
    public static string LogsDir { get; } = System.IO.Path.Join(RootDir, "logs");
    public static string TrashesDir { get; } = System.IO.Path.Join(RootDir, ".trash");

    public static string RootPath(string? argRootPath = null)
    {
        // 主动传值
        if (argRootPath is { Length: > 0 })
        {
            return argRootPath;
        }

        // Env 传值
        var envPathExists = System.IO.Path.Exists(Environment.GetEnvironmentVariable("A2CR_ROOT"));

        // 默认值
        var rootPath = (envPathExists
            ? Environment.GetEnvironmentVariable("A2CR_ROOT")
            : System.IO.Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".a2CrLauncher")) ?? throw new InvalidOperationException("没有用户 HOME 目录。");
        return rootPath;
    }

    public static void CreateDirs()
    {
        Directory.CreateDirectory(AssetsDir);
        Directory.CreateDirectory(LibrariesDir);
        Directory.CreateDirectory(LogConfigsDir);
        Directory.CreateDirectory(VersionsDir);
        Directory.CreateDirectory(MetasDir);
        
        Directory.CreateDirectory(ModsPoolDir);

        Directory.CreateDirectory(InstancesDir);
        Directory.CreateDirectory(ConfigsDir);
        Directory.CreateDirectory(TemplatesDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(TrashesDir);
    }
}