using a2Cr.Core.Java;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Tests.Java;

public sealed class JavaRuntimeTests
{
    [Fact]
    public void ReleaseFile_ParsesLegacyJavaVersion()
    {
        var home = CreateRuntime("1.8.0_412");

        var runtime = new JavaRuntimeFinder().Find([home]).Single(item =>
            string.Equals(item.Home, home, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(8, runtime.Major);
        Assert.Equal("1.8.0_412", runtime.Version);
    }

    [Fact]
    public void Select_PicksClosestRuntimeAtOrAboveRequirement()
    {
        var old = CreateRuntime("17.0.12");
        var newRuntime = CreateRuntime("21.0.4");

        var runtime = JavaRuntimeFinder.Select(20, [
            new JavaRuntime(old, 17, "17.0.12"),
            new JavaRuntime(newRuntime, 21, "21.0.4")
        ]);

        Assert.Equal(21, runtime.Major);
    }

    private static string CreateRuntime(string version)
    {
        var home = IoPath.Combine(IoPath.GetTempPath(), "a2cr-java-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(IoPath.Combine(home, "bin"));
        File.WriteAllText(IoPath.Combine(home, "release"), $"JAVA_VERSION=\"{version}\"\n");
        return home;
    }
}
