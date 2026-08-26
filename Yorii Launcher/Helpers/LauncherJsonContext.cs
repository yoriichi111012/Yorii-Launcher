using System.Text.Json.Serialization;
using System.Collections.Generic;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(InstanceMetadata))]
    [JsonSerializable(typeof(PlayerAccount))]
    [JsonSerializable(typeof(List<PlayerAccount>))]
    [JsonSerializable(typeof(MinecraftPatchNotesResponse))]
    [JsonSerializable(typeof(MinecraftReleaseNoteContent))]
    [JsonSerializable(typeof(MinecraftVersionManifestResponse))]
    [JsonSerializable(typeof(UserSettings))]
    [JsonSerializable(typeof(ThemeSettings))]
    [JsonSerializable(typeof(LoaderVersionCache))]
    [JsonSerializable(typeof(VersionIndexEntry))]
    internal sealed partial class LauncherJsonContext : JsonSerializerContext
    {
    }
}
