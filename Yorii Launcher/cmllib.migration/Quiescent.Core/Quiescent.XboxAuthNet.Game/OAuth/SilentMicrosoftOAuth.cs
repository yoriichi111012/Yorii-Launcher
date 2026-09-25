using Quiescent.XboxAuthNet.Game.Authenticators;
using Quiescent.XboxAuthNet.OAuth;
using Quiescent.XboxAuthNet.OAuth.CodeFlow.Parameters;

namespace Quiescent.XboxAuthNet.Game.OAuth;

public class SilentMicrosoftOAuth : MicrosoftOAuth
{
    public SilentMicrosoftOAuth(MicrosoftOAuthParameters parameters) : base(parameters)
    {

    }

    protected override async ValueTask<MicrosoftOAuthResponse?> Authenticate(
        AuthenticateContext context, MicrosoftOAuthParameters parameters)
    {
        context.Logger.LogSilentMicrosoftOAuth();

        var session = GetSessionFromStorage();
        if (string.IsNullOrEmpty(session?.RefreshToken))
            throw new MicrosoftOAuthException(
                "Cached RefreshToken of the user was empty. " +
                "Interactive Microsoft authentication is required.", 0);

        var apiClient = parameters.ClientInfo.CreateApiClientForOAuthCode(context.HttpClient);
        return await apiClient.RefreshToken(
            new CodeFlowRefreshTokenParameter
            {
                RefreshToken = session.RefreshToken
            }, 
            context.CancellationToken);
    }
}
