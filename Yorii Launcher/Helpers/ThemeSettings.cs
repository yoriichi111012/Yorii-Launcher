using System;

namespace Yorii_Launcher.Helpers
{
    public class ThemeSettings
    {
        public string CurrentTheme { get; set; } = "Dark";
        public string BackgroundImagePath { get; set; } = "";
        public double OverlayOpacity { get; set; } = 0.3;
        public bool OverlayBlurEnabled { get; set; } = false;
        public bool UseCustomAccentColor { get; set; } = true;
        public string CustomAccentColor { get; set; } = "#11EEFF";
        public string ActiveThemeFolder { get; set; } = "";

        public bool? ServerlistEnabled { get; set; }
        public bool? WorldlistEnabled { get; set; }
        public bool? ReleasenotesEnabled { get; set; }
        public double? CardBorderThickness { get; set; }
        public string? CardBorderColor { get; set; }
        public string? CardBackgroundColor { get; set; }
        public string? SettingscardBackgroundColor { get; set; }
        public string? SettingsexpanderHoverColor { get; set; }
        public string? SettingsexpanderPressedColor { get; set; }
        public string? SettingscardDisabledColor { get; set; }
        public string Systembackdrop { get; set; } = "mica";
    }
}
