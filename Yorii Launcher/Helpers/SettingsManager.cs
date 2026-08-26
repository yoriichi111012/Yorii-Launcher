using System;
using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace Yorii_Launcher.Helpers
{
    public static class SettingsManager
    {
        private static readonly object saveLock = new();
        private static UserSettings? current;

        // store launcher-owned state as yaml. the json path remains read-only migration input
        internal static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Yorii Launcher",
            "settings.yaml"
        );

        private static string LegacySettingsFilePath => Path.ChangeExtension(SettingsFilePath, ".json");
        // main access point for settings, lazy init so restoresettings can replace it
        public static UserSettings Current
        {
            get
            {
                current ??= new UserSettings();
                return current;
            }
        }

        // load yaml and migrate the previous json settings file once when present
        public static void RestoreSettings()
        {
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    var loaded = LauncherYaml.Deserialize<UserSettings>(File.ReadAllText(SettingsFilePath));
                    if (loaded != null)
                    {
                        current = loaded;
                        return;
                    }
                }
                catch
                {
                    Logger.Warn("Failed to load settings, starting with defaults");
                }
            }

            if (TryLoadLegacySettings(out var legacySettings))
            {
                current = legacySettings;
                SaveSettings();
                Logger.Info("Migrated settings.json to settings.yaml");
                return;
            }

            current = new UserSettings();
            MigrateFromLocalSettings();
            SaveSettings();
        }

        // save settings as yaml
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

                    File.WriteAllText(SettingsFilePath, LauncherYaml.Serialize(current));
                }
                catch
                {
                    Logger.Error("Failed to save settings");
                }
            }
        }

        // copy settings.yaml to a user chosen location
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

        private static bool TryLoadLegacySettings(out UserSettings settings)
        {
            settings = new UserSettings();
            if (!File.Exists(LegacySettingsFilePath))
                return false;

            try
            {
                settings = JsonSerializer.Deserialize(
                    File.ReadAllText(LegacySettingsFilePath),
                    LauncherJsonContext.Default.UserSettings) ?? new UserSettings();
                return true;
            }
            catch
            {
                Logger.Warn("Failed to migrate settings.json");
                return false;
            }
        }

        // migrate old localsettings registry data to the new yaml file
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
                TryMigrateBool("WorldListEnabled", v => current.WorldListEnabled = v);
                TryMigrateBool("ShowConsole", v => current.ShowConsole = v); // i mean it should count as misc. i dont really know where to put it in the settings page
                TryMigrateString("WindowBehavior", v => current.WindowBehavior = v); // same for this one
                // performance settings
                TryMigrateDouble("RamAmount", v => current.RamAmount = v);
                // version filter settings
                TryMigrateBool("ShowSnapshots", v => current.ShowSnapshots = v);
                TryMigrateBool("ShowFabric", v => current.ShowFabric = v);
                TryMigrateBool("ShowForge", v => current.ShowForge = v);
                TryMigrateBool("ShowNeoForge", v => current.ShowNeoForge = v);
                // trymigratebool("showoptifine", v => current.showoptifine = v)
                TryMigrateBool("ShowOld", v => current.ShowOld = v);
                // current account, instance and server and other account related cached stuff
                TryMigrateString("SelectedInstanceId", v => current.SelectedInstanceId = v);
                TryMigrateString("SelectedAccountId", v => current.SelectedAccountId = v);
                TryMigrateString("SelectedServerAddress", v => current.SelectedServerAddress = v);
                TryMigrateString("SelectedWorldId", v => current.SelectedWorldId = v);
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
                // happens when running unpackaged / no package identity - skip localsettings migration
                Logger.Info("Skipping LocalSettings migration: no package identity.");
            }
        }
    }
}