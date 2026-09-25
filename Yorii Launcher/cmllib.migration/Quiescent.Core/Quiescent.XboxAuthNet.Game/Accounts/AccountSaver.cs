using Quiescent.XboxAuthNet.Game.Authenticators;

namespace Quiescent.XboxAuthNet.Game.Accounts;

public class AccountSaver : IAuthenticator
{
    private readonly IXboxGameAccountManager _accountManager;

    public AccountSaver(IXboxGameAccountManager accountManager) =>
        _accountManager = accountManager;

    public ValueTask ExecuteAsync(AuthenticateContext context)
    {
        context.Logger.LogSaveAccounts();
        _accountManager.SaveAccounts();
        return new ValueTask();
    }
}