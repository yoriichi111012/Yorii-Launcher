using Quiescent.XboxAuthNet.Game.Authenticators;
using Quiescent.XboxAuthNet.Game.SessionStorages;
using Quiescent.XboxAuthNet.OAuth;
using Quiescent.XboxAuthNet.XboxLive;
using Quiescent.XboxAuthNet.XboxLive.Crypto;

namespace Quiescent.XboxAuthNet.Game.XboxAuth;

public class XboxSignedUserTokenAuth : SessionAuthenticator<XboxAuthTokens>
{
    private readonly ISessionSource<MicrosoftOAuthResponse> _oAuthSessionSource;
    private readonly IXboxRequestSigner _signer;

    public XboxSignedUserTokenAuth(
        IXboxRequestSigner signer,
        ISessionSource<MicrosoftOAuthResponse> oauthSessionSource,
        ISessionSource<XboxAuthTokens> sessionSource)
        : base(sessionSource) => 
        (_signer, _oAuthSessionSource) = (signer, oauthSessionSource);

    protected override async ValueTask<XboxAuthTokens?> Authenticate(AuthenticateContext context)
    {
        context.Logger.LogXboxSignedUserToken();

        var oAuthAccessToken = _oAuthSessionSource
            .Get(context.SessionStorage)?
            .AccessToken;

        if (string.IsNullOrEmpty(oAuthAccessToken))
            throw new XboxAuthException("OAuth access token was empty. Microsoft OAuth is required.", 0);

        var xboxAuthClient = new XboxSignedClient(_signer, context.HttpClient);
        var userToken = await xboxAuthClient.RequestSignedUserToken(oAuthAccessToken);

        var xboxTokens = GetSessionFromStorage() ?? new XboxAuthTokens();
        xboxTokens.UserToken = userToken;
        return xboxTokens;
    }
}