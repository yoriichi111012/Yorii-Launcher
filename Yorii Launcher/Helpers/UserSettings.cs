using System;
using System.Collections.Generic;
using System.IO;

namespace Yorii_Launcher.Helpers
{
    public class UserSettings
    {
        // launcher
        public string MinecraftPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        public string SelectedVersion { get; set; } = "26.2";
        public string LastSavedVersion { get; set; } = "";
        public bool InstancesEnabled { get; set; } = true;
        public bool ServerListEnabled { get; set; } = true;
        public bool WorldListEnabled { get; set; } = true;
        public double RamAmount { get; set; } = 4;

        // version filters
        public bool ShowSnapshots { get; set; } = false;
        public bool ShowFabric { get; set; } = true;
        public bool ShowForge { get; set; } = false;
        public bool ShowNeoForge { get; set; } = true;
        // public bool showoptifine { get; set; } = false
        public bool ShowOld { get; set; }

        // misc
        public bool ShowConsole { get; set; } = false;
        public string WindowBehavior { get; set; } = "None";
        public bool ShowReleaseNotesOnHome { get; set; } = true;

        // plugin view modes (0 = list, 1 = grid; grid by default)
        public int InstalledModsViewMode { get; set; } = 1;
        public int DownloadModsViewMode { get; set; } = 1;
        public int InstalledResourcePacksViewMode { get; set; } = 1;
        public int DownloadResourcePacksViewMode { get; set; } = 1;
        public int InstalledModpacksViewMode { get; set; } = 1;
        public int DownloadModpacksViewMode { get; set; } = 1;

        // selected state
        public string SelectedInstanceId { get; set; } = "";
        public string SelectedAccountId { get; set; } = "";
        public string SelectedServerAddress { get; set; } = "";
        public string SelectedWorldId { get; set; } = "";

        // experimental
        public bool ExperimentalResourcePackAnyVersion { get; set; } = false;

        // auth cache
        public string ClientToken { get; set; } = "";
        public string CachedUsername { get; set; } = "";
        public string CachedUUID { get; set; } = "";
        public string CachedAccessToken { get; set; } = "";

        // yorii skins is our cloudflare auth server worker which fetches skins from github repo
        public string? GitHubToken { get; set; }
        public string? GitHubUsername { get; set; }

        // yorii skins is our cloudflare auth server worker which fetches skins from github repo
        // only the holder of a public profile's claim token may update or
        // delete it; the token is minted by the worker on first upload
        public Dictionary<string, string> ClaimTokens { get; set; } = [];

        [System.Text.Json.Serialization.JsonIgnore]
        [YamlDotNet.Serialization.YamlIgnore]
        public bool IsGitHubLoggedIn => !string.IsNullOrEmpty(GitHubToken) && !string.IsNullOrEmpty(GitHubUsername);

        // returns the active minecraft path based on instances state
        // fall back to the global one if no instance is active
        public string GetActiveMinecraftPath()
        {
            if (InstancesEnabled)
            {
                var selectedInstance = InstanceManager.GetSelectedInstance();
                if (selectedInstance != null)
                    return selectedInstance.MinecraftPath;
            }
            return MinecraftPath;
        }

        // strips "fabric ", "forge ", "neoforge " prefix from version string
        public string GetCleanSelectedVersion()
        {
            var selected = SelectedVersion;
            if (selected.StartsWith("Fabric "))
                selected = selected["Fabric ".Length..].Trim();
            else if (selected.StartsWith("Forge "))
                selected = selected["Forge ".Length..].Trim();
            else if (selected.StartsWith("NeoForge "))
                selected = selected["NeoForge ".Length..].Trim();
            // else if (selected.startswith("optifine "))
            // selected = selected["optifine ".length..].trim()
            return selected;
        }
    }
}