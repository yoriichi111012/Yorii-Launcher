using Quiescent.XboxAuthNet.Game.Authenticators;
using Quiescent.XboxAuthNet.Game.SessionStorages;
using Quiescent.Core.Auth.Microsoft.Sessions;

namespace Quiescent.Core.Auth.Microsoft.Authenticators;

public class JEProfileValidator : SessionValidator<JEProfile>
{
    public JEProfileValidator(ISessionSource<JEProfile> sessionSource)
     : base(sessionSource)
    {

    }

    protected override ValueTask<bool> Validate(AuthenticateContext context, JEProfile profile)
    {
        var isValid = (
            !string.IsNullOrEmpty(profile.Username) && 
            !string.IsNullOrEmpty(profile.UUID));
        context.Logger.LogJEProfileValidator(isValid);
        return new ValueTask<bool>(isValid);
    }
}