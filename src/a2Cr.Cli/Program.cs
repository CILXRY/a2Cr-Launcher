using a2Cr.Core.Path;

namespace a2Cr.Cli;

class Program
{
    static async Task Main(string[] args)
    {
        var librariesDir = LauncherPaths.LibrariesDir;

        Console.WriteLine(librariesDir);
        LauncherPaths.CreateDirs();


        var r = await a2Cr.Core.Download.Download.DownloadFile(
            "https://piston-meta.mojang.com/mc/game/version_manifest.json",
            Path.Join(librariesDir, "version_manifest.json"));
        Console.WriteLine($"ok={r.Ok} error={r.ErrorMessage}");
        if (!r.Ok) Environment.ExitCode = 1;
    }
}
