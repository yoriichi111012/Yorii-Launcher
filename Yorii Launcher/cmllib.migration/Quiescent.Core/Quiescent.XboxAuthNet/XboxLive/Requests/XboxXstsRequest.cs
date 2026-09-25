using System;
using System.Net.Http;
using System.Threading.Tasks;
using Quiescent.XboxAuthNet.XboxLive.Responses;

namespace Quiescent.XboxAuthNet.XboxLive.Requests
{
    public class XboxXstsRequest : AbstractXboxAuthRequest
    {
        public const string XstsAuthorizeUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";

        public XboxXstsRequest()
        {
            RelyingParty = XboxAuthConstants.XboxLiveRelyingParty;
            ContractVersion = "1";
        }

        public string? UserToken { get; set; }
        public string? RelyingParty { get; set; }
        public string? DeviceToken { get; set; }
        public string? TitleToken { get; set; }
        public string[]? OptionalDisplayClaims { get; set; }

        protected override HttpRequestMessage BuildRequest()
        {
            if (string.IsNullOrEmpty(UserToken))
                throw new InvalidOperationException("UserToken was null");
            if (string.IsNullOrEmpty(RelyingParty))
                throw new InvalidOperationException("RelyingParty was null");
            
            var props = new System.Text.Json.Nodes.JsonObject
            {
                ["UserTokens"] = new System.Text.Json.Nodes.JsonArray(UserToken),
                ["SandboxId"] = "RETAIL"
            };
            // null entries must NOT be added: JsonObject writes explicit JSON
            // nulls (WhenWritingNull doesn't apply inside a node tree) and
            // Xbox Live rejects unknown null properties with an empty-body 400
            if (!string.IsNullOrEmpty(DeviceToken))
                props["DeviceToken"] = DeviceToken;
            if (!string.IsNullOrEmpty(TitleToken))
                props["TitleToken"] = TitleToken;
            if (OptionalDisplayClaims is { Length: > 0 })
                props["OptionalDisplayClaims"] =
                    new System.Text.Json.Nodes.JsonArray(
                        OptionalDisplayClaims.Select(x => System.Text.Json.Nodes.JsonValue.Create(x)).ToArray());

            var req = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(XstsAuthorizeUrl),
                Content = HttpHelper.CreateJsonContent(new XboxLiveAuthRequestBody
                {
                    RelyingParty = RelyingParty,
                    TokenType = "JWT",
                    Properties = props
                }),
            };

            CommonRequestHeaders.AddDefaultHeaders(req);
            return req;
        }

        public Task<XboxAuthResponse> Send(HttpClient httpClient)
        {
            return Send<XboxAuthResponse>(httpClient);
        }
    }
}