using Quiescent.XboxAuthNet.Game.Authenticators;
using Quiescent.XboxAuthNet.Game.SessionStorages;
using Quiescent.XboxAuthNet.OAuth;

namespace Quiescent.XboxAuthNet.Game.OAuth;

public class MicrosoftOAuthLoginHintValidator : ISessionValidator
{
    private ISessionSource<string> _loginHintSource;
    private readonly bool _throwWhenInvalid = false;

    public MicrosoftOAuthLoginHintValidator(bool throwWhenInvalid, ISessionSource<string> loginHintSource)
    {
        _throwWhenInvalid = throwWhenInvalid;
        _loginHintSource = loginHintSource;
    }

    public ValueTask<bool> Validate(AuthenticateContext context)
    {
        var loginHint = _loginHintSource.Get(context.SessionStorage);
        var loginHintExists = !string.IsNullOrEmpty(loginHint);
        if (_throwWhenInvalid && !loginHintExists)
            throw new MicrosoftOAuthException("LoginHint was empty.", 0);
        return new ValueTask<bool>(loginHintExists);
    }
}
