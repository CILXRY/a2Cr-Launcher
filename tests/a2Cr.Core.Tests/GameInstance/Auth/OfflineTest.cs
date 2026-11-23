using a2Cr.Core.GameInstance.Auth;
using JetBrains.Annotations;

namespace a2Cr.Core.Tests.GameInstance.Auth;

[TestSubject(typeof(Offline))]
public class OfflineTest
{
    [Fact]
    public void GenerateUuid()
    {
        var uuid = Offline.GenerateOfflineUuid("a2Cr").ToLower();

        Assert.Equal("c600d906-d748-3b36-b538-53c187a1a16e", uuid);
    }
}