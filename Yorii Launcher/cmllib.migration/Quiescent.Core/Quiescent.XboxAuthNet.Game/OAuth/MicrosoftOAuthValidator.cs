using Quiescent.XboxAuthNet.Game.Authenticators;
using Quiescent.XboxAuthNet.Game.SessionStorages;
using Quiescent.XboxAuthNet.OAuth;

namespace Quiescent.XboxAuthNet.Game.OAuth;

public class MicrosoftOAuthValidator : SessionValidator<MicrosoftOAuthResponse>
{
    public MicrosoftOAuthValidator(
        ISessionSource<MicrosoftOAuthResponse> sessionSource)
        : base(sessionSource)
    {

    }
    
    protected override ValueTask<bool> Validate(AuthenticateContext context, MicrosoftOAuthResponse session)
    {
        var result = session.Validate();
        context.Logger.LogMicrosoftOAuthValidation(result);
        return new ValueTask<bool>(result);
    }
}