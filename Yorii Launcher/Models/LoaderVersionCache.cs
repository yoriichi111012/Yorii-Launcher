using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Yorii_Launcher.Models
{
    // one entry of the shared mc-version-index. "name" is the exact version
    // list label ("fabric 26.2", "neoforge 26.2", "forge 26.2", "26.2") and
    // "type" is the loader family so the ui can filter and group it
    public sealed class VersionIndexEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = ""; // vanilla | fabric | forge | neoforge | snapshot
    }

    // the version index. the bundled seed, the local cache, the fetched github
    // index and the live probe results all share this shape so any of them can
    // be merged into the version list
    public sealed class LoaderVersionCache
    {
        [JsonPropertyName("entries")]
        public List<VersionIndexEntry> Entries { get; set; } = [];

        [JsonPropertyName("cachedAt")]
        public DateTimeOffset CachedAt { get; set; }
    }
}