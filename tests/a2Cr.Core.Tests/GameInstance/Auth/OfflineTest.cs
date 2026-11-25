using a2Cr.Core.GameInstance.Auth;
using JetBrains.Annotations;

namespace a2Cr.Core.Tests.GameInstance.Auth;

[TestSubject(typeof(Offline))]
public class OfflineTest
{
    [Fact]
    public void GenerateUuid_Auth_CurrentUuid()
    {
        var uuid = Offline.GenerateOfflineUuid("a2Cr").ToLowerInvariant();

        // 理论应该生成相同的 UUID
        // https://zh.minecraft.wiki/w/%E8%AE%A1%E7%AE%97%E5%99%A8/%E7%8E%A9%E5%AE%B6UUID?variant=zh-cn
        Assert.Equal("c600d906-d748-3b36-b538-53c187a1a16e", uuid);
    }
}
