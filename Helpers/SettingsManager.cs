using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace Yorii_Launcher.Helpers
{
    public static class SettingsManager
    {
        private static readonly object saveLock = new();
        private static UserSettings? current;

        // set settings file path to %appdata%/Yorii Launcher/settings.json
        private static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Yorii Launcher",
            "settings.json"
        );
        // main access point for settings
        public static UserSettings Current
        {
            get
            {
                current ??= new UserSettings();
                return current;
            }
        }

        // load settings from json and migrate from older localsettings model if first run (i'll remove migration in the next to next update)
        public static void RestoreSettings()
        {
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var loaded = JsonSerializer.Deserialize(json, LauncherJsonContext.Default.UserSettings);
                    if (loaded != null)
                    {
                        current = loaded;
                        return;
                    }
                }
                catch
                {
                    Debug.WriteLine("Failed to load settings so starting with defaults");
                }
            }

            current = new UserSettings();
            MigrateFromLocalSettings();
            SaveSettings();
        }

        // save settings to json
        public static void SaveSettings()
        {
            lock (saveLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(SettingsFilePath);
                    if (dir != null && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    current ??= new UserSettings();

                    var json = JsonSerializer.Serialize(current, LauncherJsonContext.Default.UserSettings);
                    // write write write
                    File.WriteAllText(SettingsFilePath, json);
                }
                catch
                {
                    Debug.WriteLine("Failed to save settings");
                }
            }
        }

        // copy settings.json to  user chosen location
        public static void ExportSettings(string destinationPath)
        {
            if (File.Exists(SettingsFilePath))
                File.Copy(SettingsFilePath, destinationPath, true);
        }

        // import settings from a user chosen file and reload
        public static void ImportSettings(string sourcePath)
        {
            File.Copy(sourcePath, SettingsFilePath, true);
            RestoreSettings();
        }

        // migrate old localsettings registry data to the new json file
        private static void MigrateFromLocalSettings()
        {
            try
            {
                var ls = ApplicationData.Current.LocalSettings.Values;
                // launcher related settings
                TryMigrateString("MinecraftPath", v => current.MinecraftPath = v);
                TryMigrateString("SelectedVersion", v => current.SelectedVersion = v);
                TryMigrateString("lastSavedVersion", v => current.LastSavedVersion = v);
                // misc settings
                TryMigrateBool("InstancesEnabled", v => current.InstancesEnabled = v);
                TryMigrateBool("ServerListEnabled", v => current.ServerListEnabled = v);
                TryMigrateBool("ShowConsole", v => current.ShowConsole = v); // i mean it should count as misc. i dont really know where to put it in the settings page.
                TryMigrateString("WindowBehavior", v => current.WindowBehavior = v); // same for this one
                // performance settings
                TryMigrateDouble("RamAmount", v => current.RamAmount = v);
                // theme and stuff
                TryMigrateString("CurrentTheme", v => current.CurrentTheme = v);
                TryMigrateString("BackgroundImagePath", v => current.BackgroundImagePath = v);
                TryMigrateDouble("OverlayOpacity", v => current.OverlayOpacity = v);
                TryMigrateBool("OverlayBlurEnabled", v => current.OverlayBlurEnabled = v);
                // version filter settings
                TryMigrateBool("ShowSnapshots", v => current.ShowSnapshots = v);
                TryMigrateBool("ShowFabric", v => current.ShowFabric = v);
                TryMigrateBool("ShowOld", v => current.ShowOld = v);
                // current account, instance and server and other account related cached stuff 
                TryMigrateString("SelectedInstanceId", v => current.SelectedInstanceId = v);
                TryMigrateString("SelectedAccountId", v => current.SelectedAccountId = v);
                TryMigrateString("SelectedServerAddress", v => current.SelectedServerAddress = v);
                TryMigrateString("ClientToken", v => current.ClientToken = v);
                TryMigrateString("CachedUsername", v => current.CachedUsername = v);
                TryMigrateString("CachedUUID", v => current.CachedUUID = v);
                TryMigrateString("CachedAccessToken", v => current.CachedAccessToken = v);

                // migrate strings
                void TryMigrateString(string key, Action<string> setter)
                {
                    if (ls.TryGetValue(key, out var val) && val is string s && !string.IsNullOrEmpty(s))
                        setter(s);
                }
                // migrate bools
                void TryMigrateBool(string key, Action<bool> setter)
                {
                    if (ls.TryGetValue(key, out var val) && val is bool b)
                        setter(b);
                }
                // migrate doubles
                void TryMigrateDouble(string key, Action<double> setter)
                {
                    if (ls.TryGetValue(key, out var val))
                        setter(Convert.ToDouble(val));
                }
            }
            catch (InvalidOperationException)
            {
                // Happens when running unpackaged / no package identity - skip LocalSettings migration
                Debug.WriteLine("Skipping LocalSettings migration: no package identity.");
            }
        }
    }
}
