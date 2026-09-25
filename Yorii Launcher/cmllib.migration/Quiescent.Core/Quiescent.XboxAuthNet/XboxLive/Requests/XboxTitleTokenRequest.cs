using System;
using System.Net.Http;
using System.Threading.Tasks;
using Quiescent.XboxAuthNet.XboxLive.Crypto;
using Quiescent.XboxAuthNet.XboxLive.Responses;

namespace Quiescent.XboxAuthNet.XboxLive.Requests
{
    public class XboxTitleTokenRequest : AbstractXboxSignedAuthRequest
    {
        public string? AccessToken { get; set; }
        public string? DeviceToken { get; set; }
        public string? TokenPrefix { get; set; } = XboxAuthConstants.XboxTokenPrefix;
        public string? RelyingParty { get; set; } = XboxAuthConstants.XboxAuthRelyingParty;

        protected override string RequestUrl => "https://title.auth.xboxlive.com/title/authenticate";
        protected override XboxLiveAuthRequestBody BuildBody(object proofKey)
        {
            if (string.IsNullOrEmpty(AccessToken))
                throw new InvalidOperationException("AccessToken was null");
            if (string.IsNullOrEmpty(RelyingParty))
                throw new InvalidOperationException("RelyingParty was null");

            var titleProps = new System.Text.Json.Nodes.JsonObject
            {
                ["AuthMethod"] = "RPS",
                ["RpsTicket"] = TokenPrefix + AccessToken,
                ["SiteName"] = "user.auth.xboxlive.com",
                ["ProofKey"] = proofKey.ToString(),
            };
            if (!string.IsNullOrEmpty(DeviceToken))
                titleProps["DeviceToken"] = DeviceToken;

            return new XboxLiveAuthRequestBody
            {
                Properties = titleProps,
                RelyingParty = RelyingParty,
                TokenType = "JWT"
            };
        }

        public Task<XboxAuthResponse> Send(HttpClient httpClient, IXboxRequestSigner signer)
        {
            return Send<XboxAuthResponse>(httpClient, signer);
        }
    }
}