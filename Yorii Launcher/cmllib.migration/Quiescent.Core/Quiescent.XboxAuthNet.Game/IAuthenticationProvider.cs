using Quiescent.XboxAuthNet.Game.Authenticators;

namespace Quiescent.XboxAuthNet.Game;

public interface IAuthenticationProvider
{
    IAuthenticator Authenticate();
    ISessionValidator CreateSessionValidator();
    IAuthenticator AuthenticateSilently();
    IAuthenticator AuthenticateInteractively();
    IAuthenticator ClearSession();
    IAuthenticator Signout();
}