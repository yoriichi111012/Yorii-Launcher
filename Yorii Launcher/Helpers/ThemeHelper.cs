using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using Windows.UI;

namespace Yorii_Launcher.Helpers
{
    public static class ThemeHelper
    {
        public static void ApplyTheme(ElementTheme theme)
        {
            if (App.MainWindow?.Content
                is FrameworkElement root)
            {
                root.RequestedTheme = theme;
            }
            var titleBar = App.MainWindow.AppWindow.TitleBar;
            var accent = AccentThemeManager.CurrentAccent;

            switch (theme)
            {
                case ElementTheme.Light:
                    titleBar.ButtonForegroundColor = Colors.Black;
                    titleBar.ButtonHoverForegroundColor = Colors.Black;
                    titleBar.ButtonPressedForegroundColor = Colors.Black;

                    titleBar.ButtonHoverBackgroundColor = WithAlpha(accent, 20);
                    titleBar.ButtonPressedBackgroundColor = WithAlpha(accent, 40);
                    break;

                case ElementTheme.Dark:
                    titleBar.ButtonForegroundColor = Colors.White;
                    titleBar.ButtonHoverForegroundColor = Colors.White;
                    titleBar.ButtonPressedForegroundColor = Colors.White;

                    titleBar.ButtonHoverBackgroundColor = WithAlpha(accent, 20);
                    titleBar.ButtonPressedBackgroundColor = WithAlpha(accent, 40);
                    break;

                default:
                    ApplyTheme(Application.Current.RequestedTheme == ApplicationTheme.Light
                        ? ElementTheme.Light
                        : ElementTheme.Dark);
                    return;
            }

            ApplySystemBackdrop();
        }

        // avoid re-assigning the same backdrop - re-creating the micacontroller
        // on every theme/image toggle was the source of the 0xc0000005 on close
        private static string? _lastBackdropState;

        public static void ApplySystemBackdrop()
        {
            if (App.MainWindow is not MainWindow window) return;

            string target = ThemeManager.Current.Systembackdrop?.ToLowerInvariant() ?? "mica";
            if (target == _lastBackdropState) return;

            switch (target)
            {
                case "none":
                    window.SystemBackdrop = null;
                    break;
                case "micaalt":
                    App.Mica.Kind = MicaKind.BaseAlt;
                    window.SystemBackdrop = App.Mica;
                    break;
                default:
                    App.Mica.Kind = MicaKind.Base;
                    window.SystemBackdrop = App.Mica;
                    break;
            }

            _lastBackdropState = target;
        }

        public static void ApplySavedTheme()
        {
            ThemeManager.ApplyThemeOverrides(ThemeManager.Current);
            var theme = ThemeManager.Current.CurrentTheme;
            switch (theme)
            {
                case "Light":
                    ApplyTheme(ElementTheme.Light);
                    break;
                case "Dark":
                    ApplyTheme(ElementTheme.Dark);
                    break;
                default:
                    ApplyTheme(ElementTheme.Default);
                    break;
            }
        }

        public static void SaveTheme(string theme)
        {
            ThemeManager.Current.CurrentTheme = theme;
            ThemeManager.SaveSettings();
        }

        public static ElementTheme GetCurrentTheme()
        {
            // resolve "system" to whatever the actual os theme is so dialogs get the right background
            return ThemeManager.Current.CurrentTheme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => Application.Current.RequestedTheme == ApplicationTheme.Light
                    ? ElementTheme.Light
                    : ElementTheme.Dark
            };
        }

        private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}