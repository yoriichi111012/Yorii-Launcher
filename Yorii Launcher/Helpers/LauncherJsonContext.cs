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
    [JsonSerializable(typeof(GitHubPutContentPayload))]
    [JsonSerializable(typeof(GitHubDeleteContentPayload))]
    [JsonSerializable(typeof(OAuthCodeRequest))]
    [JsonSerializable(typeof(SkinUploadPayload))]
    [JsonSerializable(typeof(CapeUploadPayload))]
    [JsonSerializable(typeof(HeartbeatPayload))]
    [JsonSerializable(typeof(SkinIndexSnapshot))]
    [JsonSerializable(typeof(CslProfilePayload))]
    internal sealed partial class LauncherJsonContext : JsonSerializerContext
    {
    }
}
