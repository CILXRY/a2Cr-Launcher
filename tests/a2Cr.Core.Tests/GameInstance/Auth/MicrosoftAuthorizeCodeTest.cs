using a2Cr.Core.GameInstance.Auth;
using JetBrains.Annotations;
using Xunit.Abstractions;

namespace a2Cr.Core.Tests.GameInstance.Auth;

[TestSubject(typeof(MicrosoftAuthorizeCode))]
public class MicrosoftAuthorizeCodeTest(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task Gat_RefreshToken()
    {
        HttpClient client = new HttpClient();
        var res = await MicrosoftAuthorizeCode.LoginMicrosoftAccount();
        testOutputHelper.WriteLine(res);
        ;
    }
}