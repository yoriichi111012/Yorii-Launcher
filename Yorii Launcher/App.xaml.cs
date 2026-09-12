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
            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve; // for webview2 login
        }

        // last-resort crash recorder: without this, XAML-thread crashes die
        // silently (or only in Event Viewer). the full exception incl. stack
        // lands in LocalFolder/Logs/logs.txt — paste it when reporting.
        private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                Logger.Error($"Unhandled exception (Handled={e.Handled}): {e.Exception}");
                if (e.Exception?.InnerException is not null)
                    Logger.Error($"Inner: {e.Exception.InnerException}");
            }
            catch
            {
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "WebView2 assemblies are loaded from the NuGet cache at runtime for OAuth login; they are not part of the trimmed app graph.")]
        private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name);
            if (name.Name == "Microsoft.Web.WebView2.Core" || name.Name == "Microsoft.Web.WebView2.WinForms")
            {
                // TEMP-DIAG(webview): prove whether this 0.9-era hack fires at all
                Logger.Info($"[login-ui] AssemblyResolve fired for {args.Name} (from {args.RequestingAssembly?.GetName().Name})");
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
                        {
                            Logger.Info($"[login-ui] AssemblyResolve loading {managed}");
                            return Assembly.LoadFrom(managed);
                        }
                    }
                }
                Logger.Info("[login-ui] AssemblyResolve found nothing");
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
            // explicit source-gen JSON registration for NativeAOT certainty:
            // ModuleInitializers alone are fragile under trimming (a trimmed
            // initializer = empty registry = JsonTypeInfo failures at runtime,
            // e.g. LatestVersion on Play). statically-called methods can never
            // be trimmed away. safe to run alongside the initializers.
            Quiescent.Core.Json.CoreJsonBootstrap.EnsureRegistered();
            Quiescent.XboxAuthNet.Json.XboxAuthJsonBootstrap.EnsureRegistered();
            Quiescent.XboxAuthNet.Game.Json.GameJsonBootstrap.EnsureRegistered();
            Quiescent.Core.Auth.Microsoft.Json.AuthMicrosoftJsonBootstrap.EnsureRegistered();
            Quiescent.Core.Installer.Forge.Json.ForgeJsonBootstrap.EnsureRegistered();
            Quiescent.Core.Installer.NeoForge.Json.NeoForgeJsonBootstrap.EnsureRegistered();
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