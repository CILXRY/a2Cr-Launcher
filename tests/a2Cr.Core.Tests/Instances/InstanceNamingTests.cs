using a2Cr.Core.Instances;

namespace a2Cr.Core.Tests.Instances;

public sealed class InstanceNamingTests
{
    [Fact]
    public void Slug_LowercasesAndReplacesPunctuation()
    {
        Assert.Equal("mechanical-pack", InstanceNaming.Slug("  Mechanical Pack!  "));
    }

    [Fact]
    public void Slug_FallsBackToRandomWhenNameHasNoAscii()
    {
        var folder = InstanceNaming.Slug("机械动力");

        Assert.Equal(6, folder.Length);
        Assert.All(folder, character => Assert.True(char.IsAsciiHexDigit(character)));
    }

    [Fact]
    public void Origin_ReplacesInvalidFileCharactersAndReservedNames()
    {
        Assert.Equal("a_b_c", InstanceNaming.Origin("a/b:c"));
        Assert.Equal("_CON", InstanceNaming.Origin("CON"));
        Assert.Equal("name", InstanceNaming.Origin("name..."));
    }

    [Fact]
    public void CreateFolderName_AddsSuffixWhenFolderExists()
    {
        var existing = new HashSet<string>(["pack", "pack-2"], StringComparer.OrdinalIgnoreCase);

        var folder = InstanceNaming.CreateFolderName("Pack", "slug", existing.Contains);

        Assert.Equal("pack-3", folder);
    }

    [Fact]
    public void CreateFolderName_OpaqueRetriesUntilUnique()
    {
        var attempts = 0;
        var folder = InstanceNaming.CreateFolderName("Pack", "opaque", _ => attempts++ < 1);

        Assert.StartsWith("inst-", folder);
        Assert.Equal(13, folder.Length);
    }
}
