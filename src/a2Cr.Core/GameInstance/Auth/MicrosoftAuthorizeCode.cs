using a2Cr.Core.Launcher.Definitions;

namespace a2Cr.Core.GameInstance.Auth;

/// <summary>
/// 授权代码登录
/// <remarks>见：https://zh.minecraft.wiki/w/Tutorial:%E7%BC%96%E5%86%99%E5%90%AF%E5%8A%A8%E5%99%A8?variant=zh-cn#%E6%8E%88%E6%9D%83%E4%BB%A3%E7%A0%81%E7%99%BB%E5%BD%95</remarks>
/// </summary>
public class MicrosoftAuthorizeCode
{
    private const string GetAccessTokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    public static string LoginMicrosoftAccount(HttpClient client)
    {
        // Open:
        // https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize
        // ?client_id= //上面Azure应用程序的client_id
        // &response_type=code
        // &redirect_uri= //需要在上面Azure应用程序中添加，一般采取本地搭建临时Web服务器的方式并将回调URI设为本地地址
        // &response_mode=query
        // &scope=XboxLive.signin offline_access

        // 提取 URL

        return "";
    }

    /// <summary>
    /// 通过 登录 来获取 AccessToken
    /// </summary>
    /// <param name="client"><c>HttpClient</c></param>
    /// <param name="code">通过<c>LoginMicrosoftAccount</c>获得的授权码</param>
    /// <param name="redirectUrl">回调 URL</param>
    /// <returns></returns>
    public async Task<string> GetAccessToken(HttpClient client, string code, string redirectUrl)
    {
        var commited = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("client_id", LauncherDefinitions.AzureAppId),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("redirect_uri", redirectUrl),
            new KeyValuePair<string, string>("scope", "XboxLive.signin offline_access"),
        ]);
        var response = await client
            .PostAsync(GetAccessTokenUrl, commited);

        var result = await response.Content.ReadAsStringAsync();

        return result;
    }

    /// <summary>
    /// 通过 RefreshToken 来获取 AccessToken
    /// </summary>
    /// <param name="client"><c>HttpClient</c></param>
    /// <param name="refreshToken">用于刷新的 Token</param>
    /// <returns></returns>
    public async Task<string> GetAccessToken(HttpClient client, string refreshToken)
    {
        var commited = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("client_id", LauncherDefinitions.AzureAppId),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("scope", "XboxLive.signin offline_access"),
        ]);

        var response = await client
            .PostAsync(GetAccessTokenUrl, commited);

        var result = await response.Content.ReadAsStringAsync();

        return result;
    }
}
