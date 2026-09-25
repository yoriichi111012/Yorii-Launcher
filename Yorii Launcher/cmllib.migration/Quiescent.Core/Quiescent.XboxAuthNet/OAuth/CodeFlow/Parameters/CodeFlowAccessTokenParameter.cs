using System.Text.Json.Serialization;

namespace Quiescent.XboxAuthNet.OAuth.CodeFlow.Parameters;

public class CodeFlowAccessTokenParameter : CodeFlowParameter
{
    public const string DefaultClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";


    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("redirect_uri")]
    public string? RedirectUrl { get; set; }

    [JsonPropertyName("grant_type")]
    public string? GrantType { get; set; }

    [JsonPropertyName("code_verifier")]
    public string? CodeVerifier { get; set; }

    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("client_assertion_type")]
    public string? ClientAssertionType { get; set; }

    [JsonPropertyName("client_assertion")]
    public string? CilentAssertion { get; set; }

    public override Dictionary<string, string?> ToQueryDictionary()
    {
        var query = base.ToQueryDictionary();
        AddIfSet(query, "code", Code);
        AddIfSet(query, "redirect_uri", RedirectUrl);
        AddIfSet(query, "grant_type", GrantType);
        AddIfSet(query, "code_verifier", CodeVerifier);
        AddIfSet(query, "client_secret", ClientSecret);
        AddIfSet(query, "client_assertion_type", ClientAssertionType);
        AddIfSet(query, "client_assertion", CilentAssertion);
        return query;
    }
}
