using System.Text.Json.Serialization;

namespace Quiescent.XboxAuthNet.OAuth.CodeFlow.Parameters;

public class CodeFlowParameter
{
    public string? Tenant { get; set; }

    [JsonPropertyName("client_id")]
    public string? ClientId { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    public Dictionary<string, string> ExtraQueries { get; } = new();

    // virtual + explicitly overridden per parameter type: the old
    // GetType().GetProperties() reflection breaks under trimming/NativeAOT
    // (trimmed properties vanish from the query -> live.com 400s with an
    // empty body during token exchange)
    public virtual Dictionary<string, string?> ToQueryDictionary()
    {
        var query = new Dictionary<string, string?>();
        AddCommonQueries(query);
        foreach (var kv in ExtraQueries)
        {
            query[kv.Key] = kv.Value;
        }
        return query;
    }

    protected void AddCommonQueries(Dictionary<string, string?> query)
    {
        AddIfSet(query, "client_id", ClientId);
        AddIfSet(query, "scope", Scope);
    }

    protected static void AddIfSet(Dictionary<string, string?> query, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            query[name] = value;
    }
}
