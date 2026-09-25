using Quiescent.XboxAuthNet.Game;
using Quiescent.XboxAuthNet.Game.Authenticators;
using Quiescent.XboxAuthNet.Game.OAuth;
using Quiescent.XboxAuthNet.OAuth.CodeFlow;
using Quiescent.XboxAuthNet.OAuth.CodeFlow.Parameters;
using System;

namespace Yorii_Launcher.Helpers;

// Same pipeline as MicrosoftOAuthCodeFlowProvider, except every flow leg
// that can show a browser (interactive login, browser sign-out) is wired to
// the WinUI WebView2 surface instead of the legacy WinForms broker, which
// cannot work in trimmed/NativeAOT builds. Silent/validation legs are
// untouched.
internal sealed class WinUIOAuthProvider : IAuthenticationProvider
{
    private readonly MicrosoftOAuthBuilder _oauth;

    public WinUIOAuthProvider(MicrosoftOAuthClientInfo clientInfo) =>
        _oauth = new MicrosoftOAuthBuilder(clientInfo);

    public IAuthenticator Authenticate() => _oauth.CodeFlow(WithWinUI);

    public IAuthenticator AuthenticateInteractively() =>
        _oauth.Interactive(WithWinUI, new CodeFlowAuthorizationParameter());

    public IAuthenticator AuthenticateSilently() => _oauth.Silent();

    public IAuthenticator ClearSession() => _oauth.Signout();

    public IAuthenticator Signout() => _oauth.SignoutWithBrowser(WithWinUI);

    public ISessionValidator CreateSessionValidator() => _oauth.Validator();

    private static void WithWinUI(CodeFlowBuilder builder)
    {
        builder.WithUITitle("Sign in with Microsoft");
        builder.WithWebUI(options => new WinUILoginWebUI(options));
    }
}
