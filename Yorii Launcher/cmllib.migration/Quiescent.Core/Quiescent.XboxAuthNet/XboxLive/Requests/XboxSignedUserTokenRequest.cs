using System;
using System.Net.Http;
using System.Threading.Tasks;
using Quiescent.XboxAuthNet.XboxLive.Crypto;
using Quiescent.XboxAuthNet.XboxLive.Responses;

namespace Quiescent.XboxAuthNet.XboxLive.Requests
{
    public class XboxSignedUserTokenRequest : AbstractXboxSignedAuthRequest
    {
        public string? RelyingParty { get; set; } = XboxAuthConstants.XboxAuthRelyingParty;
        public string? TokenPrefix { get; set; } = XboxAuthConstants.XboxTokenPrefix;
        public string? AccessToken { get; set; }

        protected override string RequestUrl => "https://user.auth.xboxlive.com/user/authenticate";
        protected override XboxLiveAuthRequestBody BuildBody(object proofKey)
        {
            if (string.IsNullOrEmpty(AccessToken))
                throw new InvalidOperationException("AccessToken was null");
            if (string.IsNullOrEmpty(RelyingParty))
                throw new InvalidOperationException("RelyingParty was null");

            return new XboxLiveAuthRequestBody
            {
                RelyingParty = RelyingParty,
                TokenType = "JWT",
                Properties = new System.Text.Json.Nodes.JsonObject
                {
                    ["AuthMethod"] = "RPS",
                    ["SiteName"] = "user.auth.xboxlive.com",
                    ["RpsTicket"] = TokenPrefix + AccessToken
                }
            };
        }

        public Task<XboxAuthResponse> Send(HttpClient httpClient, IXboxRequestSigner signer)
        {
            return Send<XboxAuthResponse>(httpClient, signer);
        }
    }
}