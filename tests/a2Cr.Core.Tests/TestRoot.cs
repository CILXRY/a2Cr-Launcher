using a2Cr.Core.Launcher.Definitions;

namespace a2Cr.Core.Tests;

/// <summary>每个测试独占一个临时数据根目录，结束后尽量清理。</summary>
public sealed class TestRoot : IDisposable
{
    public TestRoot(string prefix = "a2cr-tests")
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public PathsDefinitions CreatePaths()
    {
        var paths = new PathsDefinitions(Root);
        paths.CreateDirs();
        return paths;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
