using System.IO.Compression;
using a2Cr.Core.Launch;
using IoPath = System.IO.Path;

namespace a2Cr.Core.Tests.Launch;

public sealed class NativeLibraryExtractorTests
{
    [Fact]
    public void Extract_SkipsMetaInfAndRejectsPathEscape()
    {
        var root = IoPath.Combine(IoPath.GetTempPath(), "a2cr-native-tests", Guid.NewGuid().ToString("N"));
        var archive = IoPath.Combine(root, "native.jar");
        var target = IoPath.Combine(root, "natives");
        Directory.CreateDirectory(root);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            Add(zip, "META-INF/MANIFEST.MF", "ignored");
            Add(zip, "bin/native.dll", "native");
        }

        NativeLibraryExtractor.Extract(archive, target);

        Assert.False(File.Exists(IoPath.Combine(target, "META-INF", "MANIFEST.MF")));
        Assert.Equal("native", File.ReadAllText(IoPath.Combine(target, "bin", "native.dll")));
    }

    [Fact]
    public void Extract_RejectsTraversalEntry()
    {
        var root = IoPath.Combine(IoPath.GetTempPath(), "a2cr-native-tests", Guid.NewGuid().ToString("N"));
        var archive = IoPath.Combine(root, "native.jar");
        Directory.CreateDirectory(root);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) Add(zip, "../outside.dll", "bad");

        Assert.Throws<a2Cr.Core.Errors.LauncherException>(() => NativeLibraryExtractor.Extract(archive, IoPath.Combine(root, "natives")));
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }
}
