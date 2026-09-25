using System.Text.Json.Serialization;

namespace Quiescent.Core.Files;

public record AssetMetadata : MFileMetadata
{
    [JsonPropertyName("totalSize")]
    public long TotalSize { get; set; }   
}