using System.Text;
using System.Text.Json.Nodes;
using a2Cr.Core.Errors;
using a2Cr.Core.Storage;

namespace a2Cr.Core.Tests.Storage;

public sealed class AtomicJsonTests
{
    [Fact]
    public async Task ReadObjectAsync_AcceptsUtf8Bom()
    {
        using var root = new TestRoot();
        var path = System.IO.Path.Combine(root.Root, "with-bom.json");
        var bytes = Encoding.UTF8.Preamble.ToArray()
            .Concat(Encoding.UTF8.GetBytes("{\"name\":\"机械动力\"}"))
            .ToArray();
        await File.WriteAllBytesAsync(path, bytes);

        var result = await AtomicJson.ReadObjectAsync(path, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("机械动力", result!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReadObjectAsync_ReturnsNullWhenMissing()
    {
        using var root = new TestRoot();

        var result = await AtomicJson.ReadObjectAsync(
            System.IO.Path.Combine(root.Root, "missing.json"), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadObjectAsync_ReportsCorruptJsonAsInstanceBroken()
    {
        using var root = new TestRoot();
        var path = System.IO.Path.Combine(root.Root, "broken.json");
        await File.WriteAllTextAsync(path, "{broken");

        var exception = await Assert.ThrowsAsync<InstanceException>(
            () => AtomicJson.ReadObjectAsync(path, CancellationToken.None));

        Assert.Equal("instance_broken", exception.Code);
    }

    [Fact]
    public async Task WriteObjectAsync_PreservesUnknownFieldsAndLeavesNoTempFile()
    {
        using var root = new TestRoot();
        var path = System.IO.Path.Combine(root.Root, "nested", "manifest.json");
        var value = new JsonObject
        {
            ["format"] = 1,
            ["files"] = new JsonArray(),
            ["future_field"] = new JsonObject { ["keep"] = true }
        };

        await AtomicJson.WriteObjectAsync(path, value, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        var reloaded = await AtomicJson.ReadObjectAsync(path, CancellationToken.None);
        Assert.True(reloaded!["future_field"]!["keep"]!.GetValue<bool>());
    }
}
