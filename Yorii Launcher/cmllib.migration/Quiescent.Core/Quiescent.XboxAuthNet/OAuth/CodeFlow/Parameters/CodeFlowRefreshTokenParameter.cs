using System.Text.Json.Serialization;

namespace Quiescent.XboxAuthNet.OAuth.CodeFlow.Parameters;

public class CodeFlowRefreshTokenParameter : CodeFlowParameter
{
    [JsonPropertyName("grant_type")]
    public string? GrantType { get; set; }

    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    public override Dictionary<string, string?> ToQueryDictionary()
    {
        var query = base.ToQueryDictionary();
        AddIfSet(query, "grant_type", GrantType);
        AddIfSet(query, "client_secret", ClientSecret);
        AddIfSet(query, "refresh_token", RefreshToken);
        return query;
    }
}
