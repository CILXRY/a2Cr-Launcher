using System.Text.Json.Nodes;
using a2Cr.Core.Meta;

namespace a2Cr.Core.Tests.Meta;

public sealed class LibraryResolverTests
{
    [Fact]
    public void MavenCoordinate_IsConvertedToPath()
    {
        var library = JsonNode.Parse("{\"name\":\"org.example:sample:1.2.3:tests\"}")!.AsObject();

        Assert.Equal("org/example/sample/1.2.3/sample-1.2.3-tests.jar", LibraryResolver.GetRelativePath(library));
    }

    [Fact]
    public void ArtifactPath_TakesPriorityOverCoordinate()
    {
        var library = JsonNode.Parse("{\"name\":\"org.example:sample:1.2.3\",\"downloads\":{\"artifact\":{\"path\":\"custom/sample.jar\"}}}")!.AsObject();

        Assert.Equal("custom/sample.jar", LibraryResolver.GetRelativePath(library));
    }
}
