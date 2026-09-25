using System.Text.Json.Serialization;

namespace CmlLib.Core.Installer.NeoForge.Models;

internal class NeoForgeManifest
{
    [JsonPropertyName("isSnapshot")]
    public bool IsSnapshot { get; set; }

    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; }
}