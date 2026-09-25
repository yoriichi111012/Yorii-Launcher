using Quiescent.XboxAuthNet.OAuth.CodeFlow.Parameters;

namespace Quiescent.XboxAuthNet.OAuth.CodeFlow;

public interface ICodeFlowApiClient
{
    string CreateAuthorizeCodeUrl(CodeFlowAuthorizationParameter parameter);

    string CreateSignoutUrl();

    Task<MicrosoftOAuthResponse> GetAccessToken(
        CodeFlowAccessTokenParameter parameter,
        CancellationToken cancellationToken);

    Task<MicrosoftOAuthResponse> RefreshToken(
        CodeFlowRefreshTokenParameter parameter,
        CancellationToken cancellationToken);
}