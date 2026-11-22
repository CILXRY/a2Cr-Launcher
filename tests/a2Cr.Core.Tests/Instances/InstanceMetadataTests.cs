using System.Text.Json.Nodes;
using a2Cr.Core.Instances;

namespace a2Cr.Core.Tests.Instances;

public sealed class InstanceMetadataTests
{
    [Fact]
    public void Overrides_AreReadAndNormalized()
    {
        var metadata = InstanceMetadata.Create("demo", "Demo", "1.21.1", "1.21.1");
        metadata.Root["overrides"] = new JsonObject
        {
            ["max_memory"] = "8g",
            ["jvm_args"] = "-XX:+UseG1GC",
            ["java_major"] = 21,
            ["verify_on_launch"] = true
        };

        Assert.Equal("8G", metadata.MaxMemoryOverride);
        Assert.Equal("-XX:+UseG1GC", metadata.JvmArgsOverride);
        Assert.Equal(21, metadata.JavaMajorOverride);
        Assert.True(metadata.VerifyOnLaunch);
    }

    [Fact]
    public void Overrides_FallBackToNullWhenInvalid()
    {
        var metadata = InstanceMetadata.Create("demo", "Demo", "1.21.1", "1.21.1");
        metadata.Root["overrides"] = new JsonObject
        {
            ["max_memory"] = "abc",
            ["jvm_args"] = 42,
            ["java_major"] = "twenty-one",
            ["verify_on_launch"] = "yes"
        };

        Assert.Null(metadata.MaxMemoryOverride);
        Assert.Null(metadata.JvmArgsOverride);
        Assert.Null(metadata.JavaMajorOverride);
        Assert.False(metadata.VerifyOnLaunch);
    }

    [Fact]
    public void Overrides_MissingObjectMeansInheritGlobal()
    {
        var metadata = InstanceMetadata.Create("demo", "Demo", "1.21.1", "1.21.1");

        Assert.Null(metadata.MaxMemoryOverride);
        Assert.Null(metadata.JvmArgsOverride);
        Assert.Null(metadata.JavaMajorOverride);
        Assert.False(metadata.VerifyOnLaunch);
    }
}
