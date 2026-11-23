using a2Cr.Core.Settings;

namespace a2Cr.Core.Tests.Settings;

public sealed class MemoryValueTests
{
    [Theory]
    [InlineData("4G", "4G")]
    [InlineData("8g", "8G")]
    [InlineData("512M", "512M")]
    [InlineData("1024m", "1024M")]
    public void Normalize_AcceptsNumberWithUnit(string value, string expected)
    {
        Assert.True(MemoryValue.IsValid(value));
        Assert.Equal(expected, MemoryValue.Normalize(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("4GB")]
    [InlineData("4 G")]
    [InlineData("-1G")]
    [InlineData("4.5G")]
    public void Normalize_FallsBackForInvalidValues(string? value)
    {
        Assert.False(MemoryValue.IsValid(value));
        Assert.Equal("4G", MemoryValue.Normalize(value));
        Assert.Equal("8G", MemoryValue.Normalize(value, "8G"));
    }
}
