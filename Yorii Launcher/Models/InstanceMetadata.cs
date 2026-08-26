using System.Text.Json.Serialization;

namespace Yorii_Launcher.Models;

internal sealed class InstanceMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("iconPath")]
    public string? IconPath { get; set; }

    [JsonPropertyName("minecraftVersion")]
    public string? MinecraftVersion { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("lastPlayedAt")]
    public string? LastPlayedAt { get; set; }
}
