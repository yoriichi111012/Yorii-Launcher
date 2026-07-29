using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using System;
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
            // different mica styles and title bar button colors for light vs dark so it doesnt look wrong
            switch (theme)
            {
                case ElementTheme.Light:
                    App.Mica.Kind = MicaKind.BaseAlt;
                    titleBar.ButtonForegroundColor = Colors.Black;
                    titleBar.ButtonHoverForegroundColor = Colors.Black;
                    titleBar.ButtonPressedForegroundColor = Colors.Black;

                    titleBar.ButtonHoverBackgroundColor = Color.FromArgb(20, 0, 0, 0);
                    titleBar.ButtonPressedBackgroundColor = Color.FromArgb(40, 0, 0, 0);
                    break;

                case ElementTheme.Dark:
                    App.Mica.Kind = MicaKind.Base;
                    titleBar.ButtonForegroundColor = Colors.White;
                    titleBar.ButtonHoverForegroundColor = Colors.White;
                    titleBar.ButtonPressedForegroundColor = Colors.White;

                    titleBar.ButtonHoverBackgroundColor = Color.FromArgb(20, 255, 255, 255);
                    titleBar.ButtonPressedBackgroundColor = Color.FromArgb(40, 255, 255, 255);
                    break;

                default:
                    // system theme, resolve to actual light/dark and reapply
                    ApplyTheme(Application.Current.RequestedTheme == ApplicationTheme.Light
                        ? ElementTheme.Light
                        : ElementTheme.Dark);
                    return;
            }

            if (App.MainWindow is MainWindow window)
            {
                window.SystemBackdrop = App.Mica;
            }
        }

        public static void ApplySavedTheme()
        {
            var theme = SettingsManager.Current.CurrentTheme;
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
            SettingsManager.Current.CurrentTheme = theme;
            SettingsManager.SaveSettings();
        }

        public static ElementTheme GetCurrentTheme()
        {
            // resolve "System" to whatever the actual OS theme is so dialogs get the right background
            return SettingsManager.Current.CurrentTheme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => Application.Current.RequestedTheme == ApplicationTheme.Light
                    ? ElementTheme.Light
                    : ElementTheme.Dark
            };
        }
    }
}