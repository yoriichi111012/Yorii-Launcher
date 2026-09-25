using System.Text.Json.Serialization;

namespace Quiescent.XboxAuthNet.OAuth.CodeFlow.Parameters;

public class CodeFlowAuthorizationParameter : CodeFlowParameter
{
    /// <summary>
    /// response_type: id_token, token, code
    /// </summary>
    [JsonPropertyName("response_type")]
    public string? ResponseType { get; set; }

    /// <summary>
    /// redirect_uri
    /// </summary>
    [JsonPropertyName("redirect_uri")]
    public string? RedirectUri { get; set; }

    /// <summary>
    /// response_mode: query, fragment, form_post
    /// </summary>
    [JsonPropertyName("response_mode")]
    public string? ResponseMode { get; set; }

    /// <summary>
    /// state
    /// </summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>
    /// prompt: login, none, consent, select_account
    /// </summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    /// <summary>
    /// login_hint
    /// </summary>
    [JsonPropertyName("login_hint")]
    public string? LoginHint { get; set; }

    /// <summary>
    /// domain_hint
    /// </summary>
    [JsonPropertyName("domain_hint")]
    public string? DomainHint { get; set; }

    /// <summary>
    /// code_challenge
    /// </summary>
    [JsonPropertyName("code_challenge")]
    public string? CodeChallenge { get; set; }

    /// <summary>
    /// code_challenge_method
    /// </summary>
    [JsonPropertyName("code_challenge_method")]
    public string? CodeChallengeMethod { get; set; }

    [JsonPropertyName("nonce")]
    public string? Nonce { get; set; }

    public override Dictionary<string, string?> ToQueryDictionary()
    {
        var query = base.ToQueryDictionary();
        AddIfSet(query, "response_type", ResponseType);
        AddIfSet(query, "redirect_uri", RedirectUri);
        AddIfSet(query, "response_mode", ResponseMode);
        AddIfSet(query, "state", State);
        AddIfSet(query, "prompt", Prompt);
        AddIfSet(query, "login_hint", LoginHint);
        AddIfSet(query, "domain_hint", DomainHint);
        AddIfSet(query, "code_challenge", CodeChallenge);
        AddIfSet(query, "code_challenge_method", CodeChallengeMethod);
        AddIfSet(query, "nonce", Nonce);
        return query;
    }
}
