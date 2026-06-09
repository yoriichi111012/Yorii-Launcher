using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using Yorii_Launcher.Helpers;

namespace Yorii_Launcher
{
    public partial class App : Application
    {
        public static MicaBackdrop Mica { get; } = new();
        public static Window? MainWindow;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            SettingsManager.RestoreSettings();
            MainWindow = new MainWindow { SystemBackdrop = Mica };
            ThemeHelper.ApplySavedTheme();
            MainWindow.Activate();
            // check for updates after a short delay so it doesnt slow down startup
            _ = CheckForUpdatesOnStartup();
        }

        private static async Task CheckForUpdatesOnStartup()
        {
            await Task.Delay(1000);

            try
            {
                System.Diagnostics.Debug.WriteLine("[Update] Checking for updates on startup...");
                var updateInfo = await UpdateService.CheckForUpdateAsync();
                if (updateInfo == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Update] No update available");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Update] Update found: v{updateInfo.Version}");
                var current = UpdateService.GetCurrentVersion();
                NotificationHelper.Show(
                    "Update available",
                    $"Yorii Launcher {updateInfo.Version.Major}.{updateInfo.Version.Minor}.{updateInfo.Version.Build} is available. Open Settings to update.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Update] Startup check failed: {ex}");
            }
        }
    }
}
