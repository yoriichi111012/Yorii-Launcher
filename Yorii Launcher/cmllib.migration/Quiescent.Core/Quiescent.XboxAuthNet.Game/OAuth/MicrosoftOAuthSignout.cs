using Quiescent.XboxAuthNet.Game.Authenticators;
using Quiescent.XboxAuthNet.Game.SessionStorages;
using Quiescent.XboxAuthNet.OAuth;
using Quiescent.XboxAuthNet.OAuth.CodeFlow;

namespace Quiescent.XboxAuthNet.Game.OAuth;

public class MicrosoftOAuthSignout : MicrosoftOAuth
{
    private readonly Action<CodeFlowBuilder> builderInvoker;

    public MicrosoftOAuthSignout(
        MicrosoftOAuthParameters parameters,
        Action<CodeFlowBuilder> builderInvoker)
    : base(parameters)
    {
        this.builderInvoker = builderInvoker;
    }

    protected override async ValueTask<MicrosoftOAuthResponse?> Authenticate(
        AuthenticateContext context, MicrosoftOAuthParameters parameters)
    {
        context.Logger.LogMicrosoftOAuthSignout();

        var apiClient = parameters.ClientInfo.CreateApiClientForOAuthCode(context.HttpClient);
        var builder = new CodeFlowBuilder(apiClient);
        builderInvoker.Invoke(builder);
        var codeFlow = builder.Build();   

        await codeFlow.Signout(context.CancellationToken);
        return null;
    }
}