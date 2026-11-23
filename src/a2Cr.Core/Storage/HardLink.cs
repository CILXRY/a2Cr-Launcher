using System.Runtime.InteropServices;

namespace a2Cr.Core.Storage;

public static class HardLink
{
    public static bool TryCreate(string source, string target)
    {
        try
        {
            if (!File.Exists(source) || File.Exists(target)) return false;
            var directory = System.IO.Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            return OperatingSystem.IsWindows()
                ? CreateHardLinkWindows(target, source, IntPtr.Zero)
                : LinkUnix(source, target) == 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("Kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkWindows(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int LinkUnix(string oldPath, string newPath);
}
