using System.Text.Json;
using a2Cr.Core.GameInstance.Auth;
using JetBrains.Annotations;
using Xunit.Abstractions;

namespace a2Cr.Core.Tests.GameInstance.Auth;

[TestSubject(typeof(MicrosoftDeviceFlow))]
public class MicrosoftDeviceFlowTest(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task GetDeviceFlow()
    {
        var response = await MicrosoftDeviceFlow.GetKeyPair();
        testOutputHelper.WriteLine(response);
        var deviceId = JsonDocument.Parse(response).RootElement.GetProperty("device_code").GetString() ?? "";
        testOutputHelper.WriteLine(await MicrosoftDeviceFlow.GetAuthStatus(deviceId));
    }
}
