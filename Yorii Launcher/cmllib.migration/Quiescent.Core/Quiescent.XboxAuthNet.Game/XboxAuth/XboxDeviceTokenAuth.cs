using Quiescent.XboxAuthNet.Game.Authenticators;
using Quiescent.XboxAuthNet.Game.SessionStorages;
using Quiescent.XboxAuthNet.XboxLive;
using Quiescent.XboxAuthNet.XboxLive.Crypto;

namespace Quiescent.XboxAuthNet.Game.XboxAuth;

public class XboxDeviceTokenAuth : SessionAuthenticator<XboxAuthTokens>
{
    private readonly IXboxRequestSigner _signer;
    private readonly string _deviceType;
    private readonly string _deviceVersion;

    public XboxDeviceTokenAuth(
        string deviceType,
        string deviceVersion,
        IXboxRequestSigner signer,
        ISessionSource<XboxAuthTokens> sessionSource)
        : base(sessionSource) => 
        (_deviceType, _deviceVersion, _signer) = (deviceType, deviceVersion, signer);

    protected override async ValueTask<XboxAuthTokens?> Authenticate(AuthenticateContext context)
    {
        context.Logger.LogXboxDeviceToken();

        var xboxTokens = GetSessionFromStorage() ?? new XboxAuthTokens();
        var xboxAuthClient = new XboxSignedClient(_signer, context.HttpClient);
        xboxTokens.DeviceToken = await xboxAuthClient.RequestDeviceToken(
            _deviceType, _deviceVersion);
        return xboxTokens;
    }
}