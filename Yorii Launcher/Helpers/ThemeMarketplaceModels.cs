using System.Collections.Generic;

namespace Yorii_Launcher.Helpers;

public sealed class ThemeCatalog
{
    public List<ThemeCatalogEntry> Themes { get; set; } = [];
}

public sealed class ThemeCatalogEntry
{
    public string Theme { get; set; } = "";
    public string Author { get; set; } = "";
    public string Url { get; set; } = "";
    public string? DetailsUrl { get; set; }
    public string? BackgroundUrl { get; set; }
}

public sealed class ThemeDefinition
{
    public string ThemeMode { get; set; } = "dark";
    public string? AccentColor { get; set; }
    public string? BackgroundImage { get; set; }
    public bool? OverlayEnabled { get; set; }
    public double? OverlayOpacity { get; set; }
    public bool? OverlayBlur { get; set; }
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
    public string? Systembackdrop { get; set; }
}

public sealed class ThemeDetails
{
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? License { get; set; }
}
