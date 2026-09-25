using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
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

            // focused textbox underline lives as a gradient inside winui's own theme
            // dictionaries, recolor it through its stops instead of swapping the brush
            foreach (var key in AccentResourceMap.FocusGradientKeys)
            {
                RecolorGradient(resources, key, palette);
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
                // framework dictionaries (xamlcontrolsresources and friends) go read-only once
                // they have been used - inserting a new value there throws 0x800f0902 and the
                // app dies during OnLaunched, so only recolor the brushes already declared
                if (td[key] is SolidColorBrush brush)
                {
                    brush.Color = color;
                }
            }
        }

        // stops we already recolored, remembered so changing the accent again later can just
        // retint them instead of matching the system colors all over
        private static readonly Dictionary<GradientStop, AccentBrushRole> focusStops = [];

        // walks the gradient of that key and puts our palette shade on every stop that is
        // holding a system accent color
        private static void RecolorGradient(ResourceDictionary resources, string key, AccentPalette palette)
        {
            var ramp = SystemAccentRamp();
            var found = 0;
            foreach (var brush in FindGradients(resources, key))
            {
                foreach (var stop in brush.GradientStops)
                {
                    if (focusStops.TryGetValue(stop, out var role) || ramp.TryGetValue(stop.Color, out role))
                    {
                        focusStops[stop] = role;
                        stop.Color = ColorForRole(palette, role);
                        found++;
                    }
                }
            }

            if (found == 0)
            {
                Logger.Warn($"focus gradient {key} had no system accent stops to recolor");
            }
        }

        private static IEnumerable<LinearGradientBrush> FindGradients(ResourceDictionary resources, string key)
        {
            if (resources[key] is LinearGradientBrush root)
            {
                yield return root;
            }

            foreach (var merged in resources.MergedDictionaries)
            {
                foreach (var themeEntry in merged.ThemeDictionaries)
                {
                    if (themeEntry.Value is ResourceDictionary td && td.ContainsKey(key) && td[key] is LinearGradientBrush brush)
                    {
                        yield return brush;
                    }
                }
            }
        }

        private static Dictionary<Color, AccentBrushRole> SystemAccentRamp()
        {
            var ramp = new Dictionary<Color, AccentBrushRole>();
            void Add(Windows.UI.ViewManagement.UIColorType type, AccentBrushRole role) =>
                ramp[uiSettings.GetColorValue(type)] = role;

            Add(Windows.UI.ViewManagement.UIColorType.Accent, AccentBrushRole.Base);
            Add(Windows.UI.ViewManagement.UIColorType.AccentLight1, AccentBrushRole.Light1);
            Add(Windows.UI.ViewManagement.UIColorType.AccentLight2, AccentBrushRole.Light2);
            Add(Windows.UI.ViewManagement.UIColorType.AccentLight3, AccentBrushRole.Light3);
            Add(Windows.UI.ViewManagement.UIColorType.AccentDark1, AccentBrushRole.Dark1);
            Add(Windows.UI.ViewManagement.UIColorType.AccentDark2, AccentBrushRole.Dark2);
            Add(Windows.UI.ViewManagement.UIColorType.AccentDark3, AccentBrushRole.Dark3);
            return ramp;
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