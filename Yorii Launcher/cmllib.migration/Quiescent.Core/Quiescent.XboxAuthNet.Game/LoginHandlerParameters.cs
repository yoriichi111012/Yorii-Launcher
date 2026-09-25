using Microsoft.Extensions.Logging;
using Quiescent.XboxAuthNet.Game.Accounts;

namespace Quiescent.XboxAuthNet.Game;

public class LoginHandlerParameters
{
    public LoginHandlerParameters(
        HttpClient httpClient,
        IXboxGameAccountManager accountManager,
        ILogger logger) =>
        (HttpClient, AccountManager, Logger) = 
        (httpClient, accountManager, logger);

    public HttpClient HttpClient { get; }
    public IXboxGameAccountManager AccountManager { get; }
    public ILogger Logger { get; }
}