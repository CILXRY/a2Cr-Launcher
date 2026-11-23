using a2Cr.Core.Launcher.Definitions;

namespace a2Cr.Core.Tests.Path;

public sealed class PathsDefinitionsTests
{
    [Fact]
    public void ExplicitRoot_TakesPriority()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "a2cr-tests", Guid.NewGuid().ToString("N"));
        var paths = new PathsDefinitions(root);

        Assert.Equal(System.IO.Path.GetFullPath(root), paths.RootDir);
        paths.CreateDirs();
        Assert.True(Directory.Exists(paths.MetaDir));
        Assert.Equal(System.IO.Path.Combine(paths.MetaDir, "version_manifest_v2.json"), paths.VersionManifestPath);
    }

    [Fact]
    public void EnvironmentRoot_IsUsedWhenExplicitRootIsMissing()
    {
        var previous = Environment.GetEnvironmentVariable("A2CR_ROOT");
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "a2cr-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("A2CR_ROOT", root);
            Assert.Equal(System.IO.Path.GetFullPath(root), new PathsDefinitions().RootDir);
        }
        finally
        {
            Environment.SetEnvironmentVariable("A2CR_ROOT", previous);
        }
    }
}
