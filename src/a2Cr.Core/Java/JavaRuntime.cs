using System.Diagnostics;
using System.Text.RegularExpressions;
using a2Cr.Core.Errors;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Java;

public sealed record JavaRuntime(string Home, int Major, string Version)
{
    public string ExecutablePath
    {
        get
        {
            var executable = OperatingSystem.IsWindows() ? "java.exe" : "java";
            return IoPath.Combine(Home, "bin", executable);
        }
    }
}

public sealed class JavaRuntimeFinder
{
    public IReadOnlyList<JavaRuntime> Find(IEnumerable<string>? extraPaths = null)
    {
        var candidates = new List<string>();
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome)) candidates.Add(javaHome);

        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            foreach (var vendor in new[] { "Zulu", "Java", "Eclipse Adoptium", "Microsoft", "BellSoft", "Amazon Corretto", "Semeru" })
            {
                AddDirectories(candidates, IoPath.Combine(programFiles, vendor));
            }
        }

        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrWhiteSpace(appData)) AddDirectories(candidates, IoPath.Combine(appData, ".minecraft", "runtime"));
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            AddDirectories(candidates, IoPath.Combine(localAppData, "Packages", "Microsoft.4297127D64EC_8wekyb3d8bbwe", "LocalCache", "Local", "runtime"));
        }

        if (extraPaths is not null) candidates.AddRange(extraPaths);
        var javaOnPath = FindOnPath();
        if (javaOnPath is not null) candidates.Add(javaOnPath);

        var found = new List<JavaRuntime>();
        foreach (var candidate in candidates)
        {
            var runtime = Read(candidate);
            if (runtime is not null && found.All(item => !PathsEqual(item.Home, runtime.Home))) found.Add(runtime);
        }

        return found.OrderByDescending(item => item.Major).ThenBy(item => item.Home, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public JavaRuntime Select(int requiredMajor, IEnumerable<string>? extraPaths = null)
    {
        return Select(requiredMajor, Find(extraPaths));
    }

    public static JavaRuntime Select(int requiredMajor, IEnumerable<JavaRuntime> runtimes)
    {
        var selected = runtimes
            .Where(runtime => runtime.Major >= requiredMajor)
            .OrderBy(runtime => runtime.Major - requiredMajor)
            .ThenBy(runtime => runtime.Major)
            .FirstOrDefault();
        return selected ?? throw new JavaNotFoundException(requiredMajor);
    }

    public static string? ResolveExecutable(string path)
    {
        if (File.Exists(path)) return path;
        var executable = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var candidate = IoPath.Combine(path, "bin", executable);
        return File.Exists(candidate) ? candidate : null;
    }

    private static JavaRuntime? Read(string path)
    {
        try
        {
            var fullPath = IoPath.GetFullPath(path);
            if (!Directory.Exists(fullPath)) return null;
            var releasePath = IoPath.Combine(fullPath, "release");
            var version = File.Exists(releasePath) ? ReadRelease(releasePath) : null;
            if (version is null)
            {
                var executable = ResolveExecutable(fullPath);
                if (executable is null) return null;
                version = ReadProcessVersion(executable);
            }

            if (version is null || !TryGetMajor(version, out var major)) return null;
            return new JavaRuntime(fullPath, major, version);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadRelease(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal)) continue;
            return line["JAVA_VERSION=".Length..].Trim().Trim('"');
        }

        return null;
    }

    private static string? ReadProcessVersion(string executable)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        try
        {
            process.Start();
            var output = process.StandardError.ReadToEnd();
            process.WaitForExit(10_000);
            var match = Regex.Match(output, "version \\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool TryGetMajor(string version, out int major)
    {
        var normalized = version.StartsWith("1.", StringComparison.Ordinal) ? version[2..] : version;
        return int.TryParse(normalized.Split('.')[0], out major);
    }

    private static void AddDirectories(List<string> paths, string root)
    {
        try
        {
            if (Directory.Exists(root)) paths.AddRange(Directory.EnumerateDirectories(root));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? FindOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        var executable = OperatingSystem.IsWindows() ? "java.exe" : "java";
        foreach (var directory in path.Split(IoPath.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = IoPath.Combine(directory, executable);
            if (File.Exists(candidate)) return Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(IoPath.GetFullPath(left).TrimEnd(IoPath.DirectorySeparatorChar),
            IoPath.GetFullPath(right).TrimEnd(IoPath.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
