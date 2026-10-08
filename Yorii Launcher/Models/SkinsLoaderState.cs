using System;
using System.Text.Json.Serialization;

namespace Yorii_Launcher.Models
{
    // local-only poll state for the remotely hosted yoriiskinsloader jar.
    // the repo holds a single file - freshness comes from its http etag,
    // so publishing is just pushing the jar, nothing else to keep in sync
    public sealed class SkinsLoaderState
    {
        [JsonPropertyName("etag")]
        public string ETag { get; set; } = "";

        [JsonPropertyName("checkedAt")]
        public DateTimeOffset CheckedAt { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";
    }
}
