using System.Text.Json.Serialization;

namespace Quiescent.Core.ModLoaders.LiteLoader;

public class LiteLoaderLibrary
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}