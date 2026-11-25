using System.Net.Http.Headers;
using System.Text.Json;
using a2Cr.Core.Launcher.Definitions;

namespace a2Cr.Core.GameInstance.Auth;

public class MicrosoftDeviceFlow
{
    public static async Task<string> GetKeyPair(HttpClient client)
    {
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));

        var commited = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("client_id", LauncherDefinitions.AzureAppId),
            new KeyValuePair<string, string>("scope", "XboxLive.signin offline_access")
        ]);
        var response = await client
            .PostAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", commited);
        var result = await response
            .Content
            .ReadAsStringAsync();

        return result;
    }

    public static async Task<string> GetAuthStatus(string deviceCode, HttpClient client)
    {
        string response;
        while (true)
        {
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
            var commited = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:deviceCode"),
                new KeyValuePair<string, string>("client_id", LauncherDefinitions.AzureAppId),
                new KeyValuePair<string, string>("device_code", $"{deviceCode}")
            ]);
            response = await client
                .PostAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", commited).Result.Content
                .ReadAsStringAsync();
            var json = JsonDocument.Parse(response).RootElement;
            if (json.GetProperty("error").GetString() != null)
            {
                await Task.Delay(500);
                Console.WriteLine(response);
                continue;
            }

            break;
        }

        return response;
    }
}
