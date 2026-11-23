using System.Runtime.InteropServices;

namespace a2Cr.Core.Launcher.Platform;

public abstract class SystemInfo
{
    public static string SystemVersion { get; } =
        RuntimeInformation.OSDescription;

    public static string PlatformArchitecture { get; } =
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => "unknown"
        };

    public static string PlatformName { get; } =
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "osx" :
        OperatingSystem.IsLinux() ? "linux" : "unknown";
}