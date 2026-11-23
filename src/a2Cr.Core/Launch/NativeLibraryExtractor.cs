using System.IO.Compression;
using a2Cr.Core.Errors;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Launch;

public static class NativeLibraryExtractor
{
    public static void Extract(string archivePath, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        using var archive = ZipFile.OpenRead(archivePath);
        var root = IoPath.GetFullPath(targetDirectory) + IoPath.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            var relative = entry.FullName.Replace('/', IoPath.DirectorySeparatorChar);
            if (relative.StartsWith("META-INF" + IoPath.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(relative, "META-INF", StringComparison.OrdinalIgnoreCase)) continue;
            var destination = IoPath.GetFullPath(IoPath.Combine(targetDirectory, relative));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new LauncherException("install_incomplete", $"原生库包含非法路径：{entry.FullName}");
            }

            Directory.CreateDirectory(IoPath.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }
}
