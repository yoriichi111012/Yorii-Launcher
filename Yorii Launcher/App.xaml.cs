using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Yorii_Launcher.Helpers;

namespace Yorii_Launcher
{
    public partial class App : Application
    {
        public static MicaBackdrop Mica { get; } = new();
        public static Window? MainWindow;

        // set when the main window is closing: background async work that
        // touches the xaml thread must stop before the dispatcher is torn
        // down, otherwise its continuations crash with 0xc0000005 on exit
        public static volatile bool IsShuttingDown;

        private static Mutex? _mutex;

        public App()
        {
            InitializeComponent();
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve; // for webview2 login
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "WebView2 assemblies are loaded from the NuGet cache at runtime for OAuth login; they are not part of the trimmed app graph.")]
        private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name);
            if (name.Name == "Microsoft.Web.WebView2.Core" || name.Name == "Microsoft.Web.WebView2.WinForms")
            {
                var nugetDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget", "packages", "microsoft.web.webview2");

                if (Directory.Exists(nugetDir))
                {
                    var dllName = name.Name + ".dll";
                    foreach (var verDir in Directory.GetDirectories(nugetDir).OrderByDescending(d => d))
                    {
                        var managed = Path.Combine(verDir, "lib", "net462", dllName);
                        if (File.Exists(managed))
                            return Assembly.LoadFrom(managed);
                    }
                }
            }
            return null;
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            Logger.Info($"Yorii Launcher {UpdateService.GetCurrentVersion()} starting up");

            _mutex = new Mutex(true, "YoriiLauncher_SingleInstance", out bool createdNew);

            if (!createdNew)
            {
                // another instance is already running — find its window and bring it to foreground
                BringExistingInstanceToFront();
                Environment.Exit(0);
                return;
            }

            SettingsManager.RestoreSettings();
            ThemeManager.RestoreSettings();
            MainWindow = new MainWindow { SystemBackdrop = Mica };
            ThemeHelper.ApplySavedTheme();
            AccentThemeManager.ApplySavedAccent();
            MainWindow.Activate();

            // check for updates after a short delay so it doesnt slow down startup
            _ = CheckForUpdatesOnStartup();
            _ = EnsureYoriiSkinsLoaderInstalledOnStartup();

            // keep idle working set low: compact heap + trim once input is idle
            MemoryOptimizer.StartIdleTrimming();
        }

        private static async Task EnsureYoriiSkinsLoaderInstalledOnStartup()
        {
            await Task.Delay(1500);

            if (App.IsShuttingDown)
                return;

            try
            {
                InstanceManager.EnsureYoriiSkinsLoaderInstalled();
            }
            catch (Exception ex)
            {
                Logger.Error($"Startup yoriiSkinsLoader install failed: {ex.Message}");
            }
        }

        private static void BringExistingInstanceToFront()
        {
            var currentPid = Process.GetCurrentProcess().Id;
            var launcherProcesses = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName);
            foreach (var proc in launcherProcesses)
            {
                if (proc.Id == currentPid) continue;
                if (proc.MainWindowHandle == IntPtr.Zero) continue;

                NativeMethods.ShowWindow(proc.MainWindowHandle, 9); // sw_restore
                NativeMethods.SetForegroundWindow(proc.MainWindowHandle);
                break;
            }
        }

        private static async Task CheckForUpdatesOnStartup()
        {
            await Task.Delay(1000);

            if (App.IsShuttingDown)
                return;

            try
            {
                Logger.Info("Checking for updates on startup...");
                var updateInfo = await UpdateService.CheckForUpdateAsync();
                if (updateInfo == null)
                {
                    Logger.Info("No update available");
                    return;
                }

                Logger.Info($"Update found: v{updateInfo.Version}");
                var current = UpdateService.GetCurrentVersion();
                NotificationHelper.Show(
                    "Update available",
                    $"Yorii Launcher {updateInfo.Version.Major}.{updateInfo.Version.Minor}.{updateInfo.Version.Build} is available. Open Settings to update.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Startup update check failed: {ex.Message}");
            }
        }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}