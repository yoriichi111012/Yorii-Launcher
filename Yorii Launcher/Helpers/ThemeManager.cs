using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Yorii_Launcher.Helpers
{
    public static class ThemeManager
    {
        private static readonly object saveLock = new();
        private static ThemeSettings? current;

        public static event Action? ThemeSettingsChanged;

        private static string ThemeFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Yorii Launcher",
            "themes.yaml"
        );

        private static string LegacyThemeFilePath => Path.ChangeExtension(ThemeFilePath, ".json");

        public static ThemeSettings Current
        {
            get
            {
                current ??= new ThemeSettings();
                return current;
            }
        }

        public static void RestoreSettings()
        {
            if (File.Exists(ThemeFilePath))
            {
                try
                {
                    var loaded = LauncherYaml.Deserialize<ThemeSettings>(File.ReadAllText(ThemeFilePath));
                    if (loaded != null)
                    {
                        current = loaded;
                        return;
                    }
                }
                catch
                {
                    Logger.Warn("Failed to load themes.yaml, starting with defaults");
                }
            }

            if (TryLoadLegacyThemeSettings(out var legacySettings))
            {
                current = legacySettings;
                SaveSettings();
                Logger.Info("Migrated themes.json to themes.yaml");
                return;
            }

            current = new ThemeSettings();
            MigrateFromUserSettings();
            SaveSettings();
        }

        public static void SaveSettings()
        {
            lock (saveLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(ThemeFilePath);
                    if (dir != null && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    current ??= new ThemeSettings();

                    File.WriteAllText(ThemeFilePath, LauncherYaml.Serialize(current));
                }
                catch
                {
                    Logger.Error("Failed to save themes.yaml");
                }
            }
        }

        public static void ExportSettings(string destinationPath)
        {
            if (File.Exists(ThemeFilePath))
                File.Copy(ThemeFilePath, destinationPath, true);
        }

        public static void ImportSettings(string sourcePath)
        {
            File.Copy(sourcePath, ThemeFilePath, true);
            RestoreSettings();
        }

        private static void MigrateFromUserSettings()
        {
            try
            {
                var settingsPath = Path.ChangeExtension(SettingsManager.SettingsFilePath, ".json");
                if (!File.Exists(settingsPath))
                    return;

                var json = File.ReadAllText(settingsPath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("CurrentTheme", out var theme) && theme.ValueKind == JsonValueKind.String)
                    current!.CurrentTheme = theme.GetString()!;
                if (root.TryGetProperty("BackgroundImagePath", out var bg) && bg.ValueKind == JsonValueKind.String)
                    current!.BackgroundImagePath = bg.GetString()!;
                if (root.TryGetProperty("OverlayOpacity", out var opacity) && (opacity.ValueKind == JsonValueKind.Number || opacity.ValueKind == JsonValueKind.String))
                {
                    if (opacity.ValueKind == JsonValueKind.Number)
                        current!.OverlayOpacity = opacity.GetDouble();
                    else if (double.TryParse(opacity.GetString(), out var parsed))
                        current!.OverlayOpacity = parsed;
                }
                if (root.TryGetProperty("OverlayBlurEnabled", out var blur) && blur.ValueKind != JsonValueKind.Null)
                    current!.OverlayBlurEnabled = blur.GetBoolean();
                if (root.TryGetProperty("UseCustomAccentColor", out var useAccent) && useAccent.ValueKind != JsonValueKind.Null)
                    current!.UseCustomAccentColor = useAccent.GetBoolean();
                if (root.TryGetProperty("CustomAccentColor", out var accent) && accent.ValueKind == JsonValueKind.String)
                    current!.CustomAccentColor = accent.GetString()!;

                Logger.Info("Migrated theme settings from settings.json to themes.yaml");
            }
            catch
            {
                Logger.Warn("Could not migrate theme settings from settings.json");
            }
        }

        public static void ApplyThemeDefinition(ThemeDefinition definition, string? localBackgroundPath)
        {
            current ??= new ThemeSettings();
            current.CurrentTheme = definition.ThemeMode.Equals("light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
            current.UseCustomAccentColor = !string.IsNullOrWhiteSpace(definition.AccentColor);

            if (!string.IsNullOrWhiteSpace(definition.AccentColor))
                current.CustomAccentColor = definition.AccentColor;
            if (definition.OverlayEnabled.HasValue)
                current.OverlayOpacity = definition.OverlayEnabled.Value ? definition.OverlayOpacity ?? current.OverlayOpacity : 0;
            else if (definition.OverlayOpacity.HasValue)
                current.OverlayOpacity = definition.OverlayOpacity.Value;
            if (definition.OverlayBlur.HasValue)
                current.OverlayBlurEnabled = definition.OverlayBlur.Value;
            if (!string.IsNullOrWhiteSpace(localBackgroundPath))
                current.BackgroundImagePath = localBackgroundPath;
            else if (!string.IsNullOrWhiteSpace(definition.BackgroundImage))
                current.BackgroundImagePath = definition.BackgroundImage;

            current.ServerlistEnabled = definition.ServerlistEnabled;
            current.WorldlistEnabled = definition.WorldlistEnabled;
            current.ReleasenotesEnabled = definition.ReleasenotesEnabled;
            current.CardBorderThickness = definition.CardBorderThickness;
            current.CardBorderColor = definition.CardBorderColor;
            current.CardBackgroundColor = definition.CardBackgroundColor;
            current.SettingscardBackgroundColor = definition.SettingscardBackgroundColor;
            current.SettingsexpanderHoverColor = definition.SettingsexpanderHoverColor;
            current.SettingsexpanderPressedColor = definition.SettingsexpanderPressedColor;
            current.SettingscardDisabledColor = definition.SettingscardDisabledColor;
            current.Systembackdrop = definition.Systembackdrop ?? "mica";

            ApplyThemeOverrides(current);

            if (current.ServerlistEnabled.HasValue)
                SettingsManager.Current.ServerListEnabled = current.ServerlistEnabled.Value;
            if (current.WorldlistEnabled.HasValue)
                SettingsManager.Current.WorldListEnabled = current.WorldlistEnabled.Value;
            if (current.ReleasenotesEnabled.HasValue)
                SettingsManager.Current.ShowReleaseNotesOnHome = current.ReleasenotesEnabled.Value;

            SaveSettings();
            ThemeSettingsChanged?.Invoke();
        }

        public static void ApplyThemeOverrides(ThemeSettings settings)
        {
            var resources = Application.Current.Resources;

            SetResourceBrush(resources, "SettingsCardBackground", settings.CardBackgroundColor);
            SetResourceBrush(resources, "SettingsCardBackgroundPointerOver", settings.SettingsexpanderHoverColor);
            SetResourceBrush(resources, "SettingsCardBackgroundPressed", settings.SettingsexpanderPressedColor);
            SetResourceBrush(resources, "SettingsCardBackgroundDisabled", settings.SettingscardDisabledColor);

            resources["SettingsCardBorderThickness"] = new Thickness(0);
        }

        private static void SetResourceBrush(ResourceDictionary resources, string key, string? hexColor)
        {
            if (string.IsNullOrWhiteSpace(hexColor))
                return;

            if (!AccentThemeManager.TryParseHexColor(hexColor, out var color))
                return;

            // trygetvalue (not the indexer): the indexer throws keynotfoundexception when the
            // key only exists inside a themedictionary (the xaml theme-dict defaults are not
            // visible to the c# indexer), which crashed theme apply for themes without a color
            if (resources.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
                brush.Color = color;
            else
                resources[key] = new SolidColorBrush(color);
        }

        private static bool TryLoadLegacyThemeSettings(out ThemeSettings settings)
        {
            settings = new ThemeSettings();
            if (!File.Exists(LegacyThemeFilePath))
                return false;

            try
            {
                settings = JsonSerializer.Deserialize(
                    File.ReadAllText(LegacyThemeFilePath),
                    LauncherJsonContext.Default.ThemeSettings) ?? new ThemeSettings();
                return true;
            }
            catch
            {
                Logger.Warn("Failed to migrate themes.json");
                return false;
            }
        }
    }
}