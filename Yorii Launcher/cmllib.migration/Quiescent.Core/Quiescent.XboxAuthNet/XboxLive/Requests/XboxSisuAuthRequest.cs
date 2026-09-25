using System;
using System.Threading.Tasks;
using System.Net.Http;
using Quiescent.XboxAuthNet.XboxLive.Responses;
using Quiescent.XboxAuthNet.XboxLive.Crypto;

namespace Quiescent.XboxAuthNet.XboxLive.Requests
{
    public class XboxSisuAuthRequest : AbstractXboxSignedAuthRequest
    {
        public string? TokenPrefix { get; set; } = XboxAuthConstants.XboxTokenPrefix;
        public string? AccessToken { get; set; }
        public string? RelyingParty { get; set; } = XboxAuthConstants.XboxLiveRelyingParty;
        public string? ClientId { get; set; }
        public string? DeviceToken { get; set; }

        protected override string RequestUrl => "https://sisu.xboxlive.com/authorize";
        protected override XboxLiveAuthRequestBody BuildBody(object proofKey)
        {
            if (string.IsNullOrEmpty(AccessToken))
                throw new InvalidOperationException("AccessToken was null");
            if (string.IsNullOrEmpty(RelyingParty))
                throw new InvalidOperationException("RelyingParty was null");

            var sisuProps = new System.Text.Json.Nodes.JsonObject
            {
                ["AccessToken"] = TokenPrefix + AccessToken,
                ["Sandbox"] = "RETAIL",
                ["UseModernGamertag"] = true,
                ["SiteName"] = "user.auth.xboxlive.com",
                ["RelyingParty"] = RelyingParty,
                ["ProofKey"] = proofKey.ToString()
            };
            if (!string.IsNullOrEmpty(ClientId))
                sisuProps["AppId"] = ClientId;
            if (!string.IsNullOrEmpty(DeviceToken))
                sisuProps["DeviceToken"] = DeviceToken;

            return new XboxLiveAuthRequestBody
            {
                Properties = sisuProps
            };
        }

        public Task<XboxSisuResponse> Send(HttpClient httpClient, IXboxRequestSigner signer)
        {
            return Send<XboxSisuResponse>(httpClient, signer);
        }
    }
}