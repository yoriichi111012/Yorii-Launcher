using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Globalization;
using Windows.UI;

namespace Yorii_Launcher.Helpers
{
    // paints the whole app with the accent colour by tweaking brushes in place so live themeresources actually update, winui 3 accent override is flaky so we do it manually
    public static class AccentThemeManager
    {
        // fallback accent when we cant read the system one
        private static readonly Color DefaultAccent = Color.FromArgb(255, 17, 238, 255);

        public static Color CurrentAccent { get; private set; } = DefaultAccent;

        private static bool accentApplied;

        public static void ApplySavedAccent()
        {
            var settings = ThemeManager.Current;
            if (settings.UseCustomAccentColor && TryParseHexColor(settings.CustomAccentColor, out var custom))
            {
                ApplyAccent(custom);
            }
            else
            {
                ApplyAccent(GetSystemAccentColor());
            }
        }

        public static void ApplyAccent(Color baseColor)
        {
            // skip redundant re-applies when the accent didnt actually change
            if (accentApplied && CurrentAccent == baseColor) return;
            accentApplied = true;
            CurrentAccent = baseColor;
            var palette = AccentColorGenerator.Generate(baseColor);
            var resources = Application.Current.Resources;

            // main bit - swap systemaccentcolor resources so every built in control picks up the new colour through the framework ratios
            foreach (var (key, role) in AccentResourceMap.SystemColorKeys)
            {
                resources[key] = ColorForRole(palette, role);
            }

            // backup - mutate app brushes in place so even stubborn controls update
            foreach (var (key, role) in AccentResourceMap.Brushes)
            {
                SetBrush(key, ColorForRole(palette, role));
            }
        }

        // uisettings construction is a slow winrt activation so cache it
        private static readonly Windows.UI.ViewManagement.UISettings uiSettings = new();

        public static Color GetSystemAccentColor()
        {
            try
            {
                return uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            }
            catch
            {
                return DefaultAccent;
            }
        }

        private static void SetBrush(string key, Color color)
        {
            var resources = Application.Current.Resources;

            // main app brush - mutate in place so baked references update too
            if (resources[key] is SolidColorBrush existing)
            {
                existing.Color = color;
            }
            else
            {
                resources[key] = new SolidColorBrush(color);
            }

            // indexer only hits app requestedtheme but we theme per root so light mode controls read from framework light dict, force update every theme dict so both stay fresh
            ApplyToThemeDictionaries(resources, key, color);
            foreach (var merged in resources.MergedDictionaries)
            {
                ApplyToThemeDictionaries(merged, key, color);
            }
        }

        private static void ApplyToThemeDictionaries(ResourceDictionary owner, string key, Color color)
        {
            foreach (var themeEntry in owner.ThemeDictionaries)
            {
                if (themeEntry.Value is not ResourceDictionary td || !td.ContainsKey(key))
                {
                    continue;
                }
                if (td[key] is SolidColorBrush brush)
                {
                    brush.Color = color;
                }
                else
                {
                    td[key] = new SolidColorBrush(color);
                }
            }
        }

        private static Color ColorForRole(AccentPalette palette, AccentBrushRole role) => role switch
        {
            AccentBrushRole.Base => palette.Base,
            AccentBrushRole.Light1 => palette.Light1,
            AccentBrushRole.Light2 => palette.Light2,
            AccentBrushRole.Light3 => palette.Light3,
            AccentBrushRole.Dark1 => palette.Dark1,
            AccentBrushRole.Dark2 => palette.Dark2,
            AccentBrushRole.Dark3 => palette.Dark3,
            AccentBrushRole.TextOnBase => palette.TextOnBase,
            _ => palette.Base
        };

        public static bool TryParseHexColor(string? hex, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex))
                return false;

            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6)
            {
                if (byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
                    byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
                    byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                {
                    color = Color.FromArgb(255, r, g, b);
                    return true;
                }
            }
            else if (hex.Length == 8)
            {
                if (byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a) &&
                    byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
                    byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
                    byte.TryParse(hex.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                {
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
            return false;
        }

        public static string ColorToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}