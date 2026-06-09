using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Yorii_Launcher.Models
{
    public sealed class MinecraftPatchNotesResponse
    {
        [JsonPropertyName("entries")]
        public List<MinecraftReleaseNote> Entries { get; set; } = [];
    }

    public sealed class MinecraftReleaseNote
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("contentPath")]
        public string ContentPath { get; set; } = "";

        [JsonPropertyName("date")]
        public DateTimeOffset Date { get; set; }

        [JsonPropertyName("shortText")]
        public string ShortText { get; set; } = "";

        [JsonIgnore]
        public bool HasChangelog => !string.IsNullOrWhiteSpace(ContentPath);

        public override string ToString()
        {
            if (string.IsNullOrWhiteSpace(Version))
                return Title;

            return HasChangelog ? Version : $"{Version} (no notes)";
        }
    }

    public sealed class MinecraftReleaseNoteContent
    {
        [JsonPropertyName("body")]
        public string Body { get; set; } = "";
    }

    public sealed class MinecraftVersionManifestResponse
    {
        [JsonPropertyName("versions")]
        public List<MinecraftVersionManifestItem> Versions { get; set; } = [];
    }

    public sealed class MinecraftVersionManifestItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("releaseTime")]
        public DateTimeOffset ReleaseTime { get; set; }
    }
}
