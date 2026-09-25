using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Quiescent.XboxAuthNet.XboxLive.Requests;

/// <summary>
/// AOT-safe replacement for anonymous-type Xbox Live auth request bodies.
/// All request shapes are { Properties {...}, RelyingParty, TokenType }.
/// </summary>
public sealed class XboxLiveAuthRequestBody
{
    [JsonPropertyName("Properties")]
    public JsonObject? Properties { get; set; }

    [JsonPropertyName("RelyingParty")]
    public string? RelyingParty { get; set; }

    [JsonPropertyName("TokenType")]
    public string? TokenType { get; set; }
}
