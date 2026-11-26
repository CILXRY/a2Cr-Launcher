using a2Cr.Core.Launcher.Definitions;
using Microsoft.Identity.Client;

namespace a2Cr.Core.GameInstance.Auth;

/// <summary>
/// 授权代码登录
/// <remarks>见：https://zh.minecraft.wiki/w/Tutorial:%E7%BC%96%E5%86%99%E5%90%AF%E5%8A%A8%E5%99%A8?variant=zh-cn#%E6%8E%88%E6%9D%83%E4%BB%A3%E7%A0%81%E7%99%BB%E5%BD%95</remarks>
/// </summary>
public class MicrosoftAuthorizeCode
{
    // 定义在 Windows 上调用 WAM 的选项
    private static readonly BrokerOptions WindowsBrokerOptions = new(BrokerOptions.OperatingSystems.Windows)
    {
        Title = LauncherDefinitions.LauncherName
    };

    private static readonly IPublicClientApplication LoginApplication =
        PublicClientApplicationBuilder
            .Create(LauncherDefinitions.AzureAppId)
            .WithAuthority("https://login.microsoftonline.com/consumers/")
            .WithDefaultRedirectUri()
            // 在 Windows 上可以调用 WAM 来选择已经登录的 Microsoft 账户
            // .WithBroker(WindowsBrokerOptions)
            .Build();

    // 权限范围
    private static readonly string[] Scopes = ["XboxLive.signin", "offline_access"];

    public static async Task<string> GetAccessTokenAsync()
    {
        AuthenticationResult result;

        // 尝试使用以及登录的账户信息
        var accounts = await LoginApplication.GetAccountsAsync();
        var existingAccount = accounts.FirstOrDefault();

        try
        {
            // 先判断已缓存的账号
            if (existingAccount != null)
                result = await LoginApplication
                    .AcquireTokenSilent(Scopes, existingAccount).ExecuteAsync();
            // 之后判断系统当前登录的账号
            else
                result = await LoginApplication
                    .AcquireTokenSilent(Scopes, PublicClientApplication.OperatingSystemAccount).ExecuteAsync();
        }
        catch (MsalUiRequiredException)
        {
            // 最后回退到浏览器登录
            result = await LoginApplication.AcquireTokenInteractive(Scopes)
                .WithPrompt(Prompt.SelectAccount)
                // .WithParentActivityOrWindow()
                .ExecuteAsync();
        }

        return result.AccessToken;
    }
}
