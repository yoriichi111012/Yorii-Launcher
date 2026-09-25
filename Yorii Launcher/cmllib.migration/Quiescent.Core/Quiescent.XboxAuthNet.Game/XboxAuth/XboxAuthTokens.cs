using System.Text.Json.Serialization;
using Quiescent.XboxAuthNet.XboxLive.Responses;

namespace Quiescent.XboxAuthNet.Game.XboxAuth;

public class XboxAuthTokens
{
    [JsonPropertyName("deviceToken")]
    public XboxAuthResponse? DeviceToken { get; set; }

    [JsonPropertyName("titleToken")]
    public XboxAuthResponse? TitleToken { get; set; }

    [JsonPropertyName("userToken")]
    public XboxAuthResponse? UserToken { get; set; }

    [JsonPropertyName("xstsToken")]
    public XboxAuthResponse? XstsToken { get; set; }

    public bool Validate() => XstsToken?.Validate() ?? false;
}