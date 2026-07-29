using System;
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
        public bool InstancesEnabled { get; set; } = false;
        public bool ServerListEnabled { get; set; } = false;
        public bool WorldListEnabled { get; set; } = false;
        public double RamAmount { get; set; } = 4;

        // appearance
        public string CurrentTheme { get; set; } = "System";
        public string BackgroundImagePath { get; set; } = "";
        public double OverlayOpacity { get; set; } = 0.7;
        public bool OverlayBlurEnabled { get; set; } = false;
        public bool UseCustomAccentColor { get; set; } = false;
        public string CustomAccentColor { get; set; } = "";

        // version filters
        public bool ShowSnapshots { get; set; } = true;
        public bool ShowFabric { get; set; } = true;
        public bool ShowOld { get; set; }

        // misc
        public bool ShowConsole { get; set; } = false;
        public string WindowBehavior { get; set; } = "None";
        public bool ShowReleaseNotesOnHome { get; set; } = true;

        // plugin view modes
        public int InstalledModsViewMode { get; set; } = 0;
        public int DownloadModsViewMode { get; set; } = 0;
        public int InstalledResourcePacksViewMode { get; set; } = 0;
        public int DownloadResourcePacksViewMode { get; set; } = 0;
        public int InstalledModpacksViewMode { get; set; } = 0;
        public int DownloadModpacksViewMode { get; set; } = 0;

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

        // strips "Fabric " prefix from version string
        public string GetCleanSelectedVersion()
        {
            var selected = SelectedVersion;
            if (selected.StartsWith("Fabric "))
                selected = selected["Fabric ".Length..].Trim();
            return selected;
        }
    }
}
