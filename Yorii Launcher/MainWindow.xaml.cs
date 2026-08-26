using Quiescent.Core;
using Quiescent.Core.Auth;
using Quiescent.Core.Installer.Forge;
using Quiescent.Core.Installer.NeoForge;
using Quiescent.Core.Installer.NeoForge.Installers;
using Quiescent.Core.ModLoaders.FabricMC;
using Quiescent.Core.ProcessBuilder;
using Quiescent.Core.VersionLoader;
using CommunityToolkit.WinUI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;
using Yorii_Launcher.Pages;
using Yorii_Launcher.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Threading;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Microsoft.UI;

namespace Yorii_Launcher
{
    public sealed partial class MainWindow : Window
    {
        // set global minecraftpath to better code structure
        private string minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();
        // names of the folders in the versions directory, used to pin installed
        // versions to the top of the list (exact name match)
        private HashSet<string> installedVersionNames = [];
        // make mainwindow accesible from other pages
        public static MainWindow? Instance { get; set; }
        public VersionViewModel VersionVM { get; } = new VersionViewModel();
        private readonly ObservableCollection<AccountComboItem> accountItems = [];
        // private double downloadprogressvalue;
        private ContentDialog? managePlayersDialog;
        private ComboBox? homeAccountComboBox;
        private ComboBox? homeVersionComboBox;
        private Button? homePlayButton;
        // private progressbar? homedownloadprogressbar;
        private object? launchButtonContent = "Play";
        private bool launchButtonIsEnabled = true;
        /* private double launchProgressOpacity;
         private double launchProgressValue;
         private bool launchProgressIsIndeterminate;*/

        // set variables for background image
        private string currentImagePath = "";

        private ComboBox accountComboBox => homeAccountComboBox ?? throw new InvalidOperationException("Home account selector is not ready.");
        private ComboBox versionComboBox => homeVersionComboBox ?? throw new InvalidOperationException("Home version selector is not ready.");
        private Button playButton => homePlayButton ?? throw new InvalidOperationException("Home play button is not ready.");
        // private progressbar downloadprogressbar => homedownloadprogressbar ?? throw new invalidoperationexception("home progress bar is not ready.");
        public MainWindow()
        {
            InitializeComponent();


            Instance = this;

            // set up the downloads flyout backing store and keep the
            // title-bar indicator in sync with download activity
            DownloadManager.Initialize(DispatcherQueue);
            downloadsListView.ItemsSource = DownloadManager.Items;
            DownloadManager.ActivityChanged += UpdateDownloadsIndicator;

            VersionVM.FilteredVersions.CollectionChanged += (_, __) =>
            {
                if (homeVersionComboBox != null)
                {
                    versionComboBox.ItemsSource = null;
                    versionComboBox.ItemsSource = VersionVM.FilteredVersions;
                }
            };

            // set window size icon and title bar
            // appwindow.resize(new windows.graphics.sizeint32(1176, 661));
            SetWindowIcon();
            ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;

            SetTitleBar(titleBar);

            ApplyBackgroundSettings();
            mainFrame.Navigate(typeof(HomePage));
            mainFrame.Navigated += (_, __) => MemoryOptimizer.ReduceMemory();


            // just for now
            // mainframe.navigate(typeof(onboarding));
            // titlebar non visible
            // titlebar.visibility = visibility.collapsed;

            // call functions to load versions and accounts
            LoadAccounts();
            LoadVersionFilters();
            _ = LoadVersionsAsync();

            // keep the account list in sync with the skin index: a saved login
            // token means the user's private profiles should be there even on
            // cold starts, and public profiles appear without any login at all
            _ = SyncProfilesIntoAccountsAsync();

            UpdateGitHubAccountButton();

            rootGrid.ActualThemeChanged += (_, __) => ApplyBackgroundSettings();
        }

        private async Task SyncProfilesIntoAccountsAsync()
        {
            try
            {
                await SkinManager.LoadProfilesIntoAccounts();
                if (App.IsShuttingDown) return;
                // re-read accounts.json so the picker reflects the synced set
                LoadAccounts();

                // sync the selected players latest skin into all instances so
                // none of them keep showing an old skin
                if (AccountManager.GetSelectedAccount() is { } acct &&
                    acct.AccountType == PlayerAccountType.YoriiSkins)
                {
                    await SkinManager.SyncSkinToAllInstancesAsync(acct.Username);
                    if (App.IsShuttingDown) return;
                    RefreshAccounts();
                }
            }
            catch
            {
            }
        }

        public void RegisterHomeControls(ComboBox accountSelector, ComboBox versionSelector, Button launchButton, ProgressBar? progressBar = null)
        {
            CaptureLauncherControlState();

            homeAccountComboBox = accountSelector;
            homeVersionComboBox = versionSelector;
            // homedownloadprogressbar = progressbar;
            homePlayButton = launchButton;

            accountComboBox.ItemsSource = accountItems;
            versionComboBox.ItemsSource = VersionVM.FilteredVersions;
            ApplyLauncherControlState();

            var selectedAccount = AccountManager.GetSelectedAccount();
            if (selectedAccount != null)
                accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => x.Account?.Id == selectedAccount.Id);

            LoadSavedVersion();
        }

        private void CaptureLauncherControlState()
        {
            if (homePlayButton != null)
            {
                launchButtonContent = homePlayButton.Content;
                launchButtonIsEnabled = homePlayButton.IsEnabled;
            }
        }

        private void ApplyLauncherControlState()
        {
            playButton.Content = launchButtonContent ?? "Play";
            playButton.IsEnabled = launchButtonIsEnabled;
        }

        public void NavigateToSection(string tag)
        {
            switch (tag)
            {
                case "home":
                    mainFrame.Navigate(typeof(HomePage));
                    break;
                case "extensions":
                    mainFrame.Navigate(typeof(ModsPage));
                    break;
                case "releasenotes":
                    mainFrame.Navigate(typeof(ReleaseNotesPage));
                    break;
                case "instances":
                    mainFrame.Navigate(typeof(InstancesPage));
                    break;
                case "accounts":
                    mainFrame.Navigate(typeof(SkinsPage));
                    break;
                case "settings":
                    mainFrame.Navigate(typeof(SettingsPage));
                    break;
                case "themes":
                    mainFrame.Navigate(typeof(Pages.ThemesPage));
                    break;
                    // default:
                    // throw new invalidoperationexception($"unknown navigation item tag: {tag}");
            }
        }

        public void NavigateToReleaseNotes(string? version)
        {
            mainFrame.Navigate(typeof(ReleaseNotesPage), version);
        }

        public void NavigateToSettings(string? section)
        {
            mainFrame.Navigate(typeof(SettingsPage), section, new SuppressNavigationTransitionInfo());
        }

        public void NavigateToSkins()
        {
            mainFrame.Navigate(typeof(SkinsPage));
        }

        public void ApplyInstancesNavigationVisibility()
        {
            var instancesEnabled = SettingsManager.Current.InstancesEnabled;

            if (!instancesEnabled && mainFrame.CurrentSourcePageType == typeof(InstancesPage))
                SelectSection("home");
        }

        public void SelectSection(string tag)
        {
            NavigateToSection(tag);
        }

        private void LoadAccounts()
        {
            // prevent duplicate accounts by clearing before loading
            accountItems.Clear();

            foreach (var account in AccountManager.LoadAccounts())
            {
                accountItems.Add(AccountComboItem.ForAccount(account));
            }

            // add the players accounts and management options
            accountItems.Add(AccountComboItem.ManagePlayers);
            accountItems.Add(AccountComboItem.AddNew);

            _ = LoadAccountPreviewsAsync();

            if (homeAccountComboBox == null)
                return;

            accountComboBox.ItemsSource = accountItems;

            var selectedAccount = AccountManager.GetSelectedAccount();

            // fallback
            if (selectedAccount != null)
            {
                accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => x.Account?.Id == selectedAccount.Id);
            }
        }

        // renders the 16x16 player head for each account in the picker. prefers
        // the locally cached csl skin (instant, no network); falls back to the
        // published skin on the worker
        private int accountPreviewGeneration;
        private async Task LoadAccountPreviewsAsync()
        {
            var generation = ++accountPreviewGeneration;

            foreach (var item in accountItems)
            {
                if (item.Account is null) continue;
                if (App.IsShuttingDown) return;
                try
                {
                    // mojang accounts get their skin from mojangs api, yoriiskins and
                    // offline ones use the local/worker skin
                    byte[]? bytes = item.Account.AccountType == PlayerAccountType.Mojang
                        ? await SkinManager.GetMojangSkinBytesAsync(item.Account.Username)
                        : await SkinManager.GetSkinBytesLocalFirstAsync(item.Account.Username, item.Account.SkinUrl);
                    if (bytes is null || App.IsShuttingDown) continue;
                    var head = await SkinHeadRenderer.RenderHeadAsync(bytes);
                    if (head is null || App.IsShuttingDown) continue;
                    if (generation != accountPreviewGeneration) return;
                    item.PreviewImage = head;
                }
                catch { }
            }

            // winui's closed combobox presenter binds the selected item once and
            // doesn't observe inpc, so a head loaded after selection only shows
            // after the dropdown reopens. nudge the selection to re-render it
            if (!App.IsShuttingDown &&
                generation == accountPreviewGeneration &&
                accountComboBox.SelectedItem is AccountComboItem { Account: not null } selected)
            {
                accountComboBox.SelectedItem = null;
                accountComboBox.SelectedItem = selected;
            }
        }

        public void RefreshAccounts() => LoadAccounts();

        private void LoadVersionFilters()
        {
            // load filter settings
            VersionVM.ShowSnapshots = SettingsManager.Current.ShowSnapshots;
            VersionVM.ShowFabric = SettingsManager.Current.ShowFabric;
            VersionVM.ShowForge = SettingsManager.Current.ShowForge;
            VersionVM.ShowNeoForge = SettingsManager.Current.ShowNeoForge;
            // versionvm.showoptifine = settingsmanager.current.showoptifine;
            VersionVM.ShowOld = SettingsManager.Current.ShowOld;
        }

        public void ApplyBackgroundSettings()
        {
            // get current background image path
            string imagePath = ThemeManager.Current.BackgroundImagePath ?? "";
            // check if current background image path is null and whether the file exists
            bool hasImage = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath);

            // swap image first sync so overlay never beats bitmap - that order made old code flawless, async was flashing old image while blur already changed
            if (imagePath != currentImagePath)
            {
                currentImagePath = imagePath;

                if (hasImage)
                {
                    try
                    {
                        // using a filestream to prevent locking the file so the
                        // user can change or delete it without restarting the
                        // launcher - decodepixelwidth caps 4k wallpapers to 2560
                        // so the sync decode doesn't block the window
                        using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var bmp = new BitmapImage();
                        bmp.DecodePixelWidth = 2560;
                        bmp.SetSource(fs.AsRandomAccessStream());
                        backgroundImage.Source = bmp;
                    }
                    catch
                    {
                        backgroundImage.Source = null;
                        hasImage = false;
                    }
                }
                else
                {
                    backgroundImage.Source = null;
                }
            }

            backgroundImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
            overlayGrid.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;

            if (!hasImage)
            {
                overlayGrid.Opacity = 0;
                rootGrid.Background = null;
                return;
            }

            double opacity = ThemeManager.Current.OverlayOpacity;
            bool blurEnabled = ThemeManager.Current.OverlayBlurEnabled;

            // overlay is white in light mode so checking here for that
            bool isLight = ThemeHelper.GetCurrentTheme() == ElementTheme.Light;
            byte alpha = (byte)(opacity * 255);

            var fallback = isLight
                ? Windows.UI.Color.FromArgb(alpha, 255, 255, 255)
                : Windows.UI.Color.FromArgb(alpha, 0, 0, 0);

            if (blurEnabled)
            {
                // reference the xaml-defined overlay acrylic rather than constructing one in
                // code - see the brush definitions in app.xaml
                var brush = (AcrylicBrush)Application.Current.Resources[
                    isLight ? "OverlayAcrylicLight" : "OverlayAcrylicDark"];
                brush.TintOpacity = opacity;
                brush.FallbackColor = fallback;
                overlayGrid.Opacity = 1;
                overlayGrid.Background = brush;
            }
            else
            {
                overlayGrid.Opacity = 1;
                overlayGrid.Background = new SolidColorBrush(fallback);
            }
        }

        private async Task LoadVersionsAsync()
        {
            try
            {
                VersionVM.AllVersions.Clear();
                VersionVM.FilteredVersions.Clear();
                if (homeVersionComboBox != null)
                    versionComboBox.SelectedItem = null;

                var instancesEnabled = SettingsManager.Current.InstancesEnabled;
                var selectedInstance = InstanceManager.GetSelectedInstance();
                // check if instances are enabled but no instance is selected, if yes then empty version list combobox
                if (instancesEnabled && selectedInstance == null)
                {
                    VersionVM.ApplyFilters();
                    if (homeVersionComboBox != null)
                        versionComboBox.ItemsSource = VersionVM.FilteredVersions;
                    return;
                }
                // refresh the active path so it reflects the currently selected instance instead of the one captured at startup
                minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();

                // prevents crash when minecraftpath is null
                if (string.IsNullOrEmpty(minecraftPath))
                    return;

                Directory.CreateDirectory(minecraftPath);
                Directory.CreateDirectory(Path.Combine(minecraftPath, "versions"));

                // load local installed versions so they pin to the top of the list
                // their folder names are the "installed" set: an index entry is
                // pinned only when its exact name matches one of these folders
                installedVersionNames = new HashSet<string>(StringComparer.Ordinal);
                string versionsPath = Path.Combine(minecraftPath, "versions");
                foreach (var dir in Directory.GetDirectories(versionsPath))
                {
                    string versionName = Path.GetFileName(dir);
                    string jsonPath = Path.Combine(dir, versionName + ".json");

                    if (File.Exists(jsonPath))
                    {
                        installedVersionNames.Add(versionName);

                        if (versionName.Contains("OptiFine", StringComparison.OrdinalIgnoreCase)) //
                            continue;

                        VersionVM.AllVersions.Add(new VersionItem
                        {
                            Name = versionName,
                            IsInstalled = true,
                            IsFabric = versionName.StartsWith("Fabric ", StringComparison.OrdinalIgnoreCase) || versionName.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase),
                            IsForge = versionName.Contains("-forge-", StringComparison.OrdinalIgnoreCase) || versionName.Contains("-Forge", StringComparison.OrdinalIgnoreCase),
                            IsNeoForge = versionName.Contains("-neoforge-", StringComparison.OrdinalIgnoreCase),
                            IsSnapshot = versionName.Contains("snapshot"),
                            IsOld = Version.TryParse(versionName, out var v) && v < new Version(1, 16)
                        });
                    }
                }

                // instant baseline: the bundled seed (shipped with the launcher)
                // merged with the local cache, so the list is never empty even on
                // a first run with no network. no api calls happen here. the cache
                // is passed last so it wins on name conflicts with the seed
                var bundledSeed = LoaderVersionCacheService.LoadBundled();
                var cachedIndex = await LoaderVersionCacheService.LoadAsync();
                var baseline = LoaderVersionCacheService.Merge(bundledSeed, cachedIndex);
                AddIndexEntries(baseline);

                // apply filters right after the baseline load so the list shows
                // immediately instead of waiting on the network below
                VersionVM.ApplyFilters();
                if (homeVersionComboBox != null)
                    versionComboBox.ItemsSource = VersionVM.FilteredVersions;
                LoadSavedVersion();

                // refresh the index in the background: fetch the shared github
                // index first, then live-probe when that is stale, and push any
                // newly discovered versions back so all launchers benefit
                _ = RefreshVersionIndexAsync();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load versions: {ex.Message}");
            }
        }

        public void LoadSavedVersion()
        {
            if (homeVersionComboBox == null)
                return;

            var savedVersion = SettingsManager.Current.InstancesEnabled // if entances are enabled then current instance's version else null
                ? InstanceManager.GetSelectedInstanceVersion()
                : null;

            if (string.IsNullOrWhiteSpace(savedVersion) && !string.IsNullOrEmpty(SettingsManager.Current.LastSavedVersion)) // load last saved version when instance disabled
                savedVersion = SettingsManager.Current.LastSavedVersion;

            if (!string.IsNullOrWhiteSpace(savedVersion))
            {
                // check if saved version is in filtered versions list or not if yes then select it
                foreach (var item in VersionVM.FilteredVersions)
                {
                    if (item == savedVersion)
                    {
                        versionComboBox.SelectedItem = item;
                        return;
                    }
                }
            }

            if (VersionVM.FilteredVersions.Count > 0) // otherwise just select the topmost in the list
                versionComboBox.SelectedIndex = 0;
        }

        public async Task RefreshInstanceContextAsync()
        {
            // reload current selected version for instances
            await LoadVersionsAsync();
            MemoryOptimizer.ReduceMemory();
        }

        // refresh the mc-version-index in the background:
        // 1. fetch the shared github index (one request, fast)
        // 2. when it is stale, live-probe all loaders + vanilla (authoritative)
        // 3. persist the merged result locally and push it back to github so
        // other launchers pick up versions we discovered
        private async Task RefreshVersionIndexAsync()
        {
            try
            {
                if (App.IsShuttingDown) return;

                var bundledSeed = LoaderVersionCacheService.LoadBundled();
                var cachedIndex = await LoaderVersionCacheService.LoadAsync();

                // fast path: the shared index reflects what other launchers found
                // the bundled seed is the baseline so its entries survive into the
                // persisted cache even before any network data arrives
                var remoteIndex = await LoaderVersionCacheService.FetchRemoteAsync();
                var merged = LoaderVersionCacheService.Merge(bundledSeed, cachedIndex, remoteIndex);

                // slow path: when even the remote index is stale, probe the
                // loaders directly to catch brand-new versions
                bool probedFresh = false;
                if (!LoaderVersionCacheService.IsFresh(merged))
                {
                    var probed = await ProbeAllLoaderVersionsAsync();
                    merged = LoaderVersionCacheService.Merge(merged, probed);
                    // only a probe that actually found entries is worth sharing
                    probedFresh = probed.Entries.Count > 0;
                }

                if (App.IsShuttingDown) return;

                merged.Entries.RemoveAll(e => e.Type == "optifine");
                foreach (var stale in VersionVM.AllVersions.Where(v => v.Name.StartsWith("OptiFine ", StringComparison.Ordinal)).ToList())
                    VersionVM.AllVersions.Remove(stale);

                // persist the merged index and add anything we did not have yet
                // never persist an empty result - that would look "fresh" and
                // suppress re-probing for the whole freshness window
                if (merged.Entries.Count > 0)
                    await LoaderVersionCacheService.SaveAsync(merged);
                AddIndexEntries(merged);

                VersionVM.ApplyFilters();
                if (homeVersionComboBox != null)
                    versionComboBox.ItemsSource = VersionVM.FilteredVersions;
                LoadSavedVersion();

                // share freshly probed data with the shared index on github
                if (probedFresh)
                    _ = LoaderVersionCacheService.PushRemoteAsync(merged);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to refresh version index: {ex.Message}");
            }
        }

        // probe the vanilla manifest, fabric, forge and neoforge apis directly
        // probing is slow (per-version http requests to files.minecraftforge.net
        // and the neoforge maven manifest) so it only runs when the index is stale
        private async Task<LoaderVersionCache> ProbeAllLoaderVersionsAsync()
        {
            var entries = new List<VersionIndexEntry>();
            var path = new MinecraftPath(minecraftPath);
            var launcher = new MinecraftLauncher(path);

            // vanilla releases + snapshots from the mojang manifest
            try
            {
                var vanillaVersions = await launcher.GetAllVersionsAsync();
                if (App.IsShuttingDown) return new LoaderVersionCache { Entries = entries, CachedAt = DateTimeOffset.UtcNow };

                foreach (var v in vanillaVersions)
                {
                    if (v.Type != "release" && v.Type != "snapshot")
                        continue;

                    entries.Add(new VersionIndexEntry
                    {
                        Name = v.Name,
                        Type = v.Type == "snapshot" ? "snapshot" : "vanilla"
                    });
                }

                // fabric supported versions (single request)
                try
                {
                    var fabricInstaller = new FabricInstaller(HttpService.Client);
                    var fabricVersions = await fabricInstaller.GetSupportedVersionNames();

                    if (!App.IsShuttingDown)
                    {
                        foreach (var v in fabricVersions)
                        {
                            entries.Add(new VersionIndexEntry
                            {
                                Name = $"Fabric {v}",
                                Type = "fabric"
                            });
                        }
                    }
                }
                catch
                {
                    Logger.Warn("Failed to probe fabric versions");
                }

                // try
                // {
                // var optifineinstaller = new optifineinstaller(httpservice.downloadclient);
                // var optifineversions = await optifineinstaller.getoptifineversionsasync();
                // if (!app.isshuttingdown)
                // {
                // foreach (var v in optifineversions.where(x => !x.ispreviewversion))
                // {
                // entries.add(new versionindexentry
                // {
                // name = $"optifine {v.minecraftversion}",
                // type = "optifine"
                // });
                // }
                // }
                // }
                // catch
                // {
                // logger.warn("failed to probe optifine versions");
                // }

                // forge / neoforge availability (parallel probes per release)
                var forgeInstaller = new ForgeInstaller(launcher);
                var neoForgeInstaller = new NeoForgeInstaller(launcher);

                var probeReleases = vanillaVersions
                    .Where(x => x.Type == "release")
                    .Where(x => Version.TryParse(x.Name, out var rv) && rv >= new Version(1, 16))
                    .ToList();

                var gate = new SemaphoreSlim(4);
                var entriesLock = new object();

                var probeTasks = probeReleases.Select(async v =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        if (App.IsShuttingDown) return;

                        var results = await Task.WhenAll(
                            ProbeForgeAsync(forgeInstaller, v.Name),
                            ProbeNeoForgeAsync(neoForgeInstaller, v.Name));

                        if (results[0] || results[1])
                        {
                            lock (entriesLock)
                            {
                                if (results[0])
                                    entries.Add(new VersionIndexEntry { Name = $"Forge {v.Name}", Type = "forge" });
                                if (results[1])
                                    entries.Add(new VersionIndexEntry { Name = $"NeoForge {v.Name}", Type = "neoforge" });
                            }
                        }
                    }
                    catch
                    {
                        // version not supported by loader - skip
                    }
                    finally
                    {
                        gate.Release();
                    }
                });

                await Task.WhenAll(probeTasks);
                gate.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to probe versions: {ex.Message}");
            }

            return new LoaderVersionCache
            {
                Entries = entries,
                CachedAt = DateTimeOffset.UtcNow
            };
        }

        // add every entry of an index to the version list if it is not already present
        private void AddIndexEntries(LoaderVersionCache? index)
        {
            if (index == null) return;
            foreach (var entry in index.Entries)
                AddIndexEntry(entry);
        }

        // add a single index entry to the version list if it is not already present
        private void AddIndexEntry(VersionIndexEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Name))
                return;

            if (entry.Type == "optifine") return; //

            if (VersionVM.AllVersions.Any(x => x.Name == entry.Name))
                return;

            VersionVM.AllVersions.Add(new VersionItem
            {
                Name = entry.Name,
                IsInstalled = installedVersionNames.Contains(entry.Name),
                IsFabric = entry.Type == "fabric",
                IsForge = entry.Type == "forge",
                IsNeoForge = entry.Type == "neoforge",
                // isoptifine = entry.type == "optifine",
                IsSnapshot = entry.Type == "snapshot",
                IsOld = IsOldVersion(entry)
            });
        }

        // old = the loader's base minecraft version is below 1.16
        private static bool IsOldVersion(VersionIndexEntry entry)
        {
            string mcVersion = entry.Name;

            if (mcVersion.StartsWith("Fabric ", StringComparison.Ordinal))
                mcVersion = mcVersion[7..];
            else if (mcVersion.StartsWith("NeoForge ", StringComparison.Ordinal))
                mcVersion = mcVersion[9..];
            else if (mcVersion.StartsWith("Forge ", StringComparison.Ordinal))
                mcVersion = mcVersion[6..];
            // else if (mcversion.startswith("optifine ", stringcomparison.ordinal))
            // mcversion = mcversion[9..];

            return Version.TryParse(mcVersion, out var version) &&
                   version < new Version(1, 16);
        }

        private static async Task<bool> ProbeForgeAsync(ForgeInstaller installer, string mcVersion)
        {
            try
            {
                var versions = await installer.GetForgeVersions(mcVersion);
                return versions.Any();
            }
            catch
            {
                // version not supported by forge
                return false;
            }
        }

        private static async Task<bool> ProbeNeoForgeAsync(NeoForgeInstaller installer, string mcVersion)
        {
            try
            {
                var versions = await installer.GetForgeVersions(mcVersion);
                return versions.Any();
            }
            catch
            {
                // version not supported by neoforge
                return false;
            }
        }

        private static string EnsureAuthlibInjector()
        {
            // ensure authlib-injector.jar file exists in game folder if not then copy it from the launcher directory
            // programdata is used cause no spaces are there in address because trying to load from an address with spaces doesn't work
            string launcherDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Yorii Launcher");
            Directory.CreateDirectory(launcherDir);
            // current authlib version is 1.2.7
            string injectorPath = Path.Combine(launcherDir, "authlib-injector.jar"); // in launcher directory
            string jarPath = Path.Combine(AppContext.BaseDirectory, "authlib-injector.jar"); // in programdata

            if (!File.Exists(jarPath))
                throw new FileNotFoundException("authlib-injector.jar missing in launcher directory", jarPath);

            // only copy if the file size is different
            if (File.Exists(injectorPath) && new FileInfo(jarPath).Length == new FileInfo(injectorPath).Length)
                return injectorPath;

            File.Copy(jarPath, injectorPath, true);
            return injectorPath;
        }

        // assets/skins is where minecraft and yoriiskinsloader cache skins, wipe it every launch so fresh skin always loads
        // yoriiskinsloader is a fork of customskinloader optimized for faster skin loading and other improvements
        // it still caches to assets/skins for speed but that cache can hold an old skin if the url stays same, so clearing it forces a refetch from the worker
        // it also keeps its own cache under CustomSkinLoader/caches which does the same thing
        private void ClearAssetsSkinsCache(string minecraftPath)
        {
            try
            {
                var skinsCache = Path.Combine(minecraftPath, "assets", "skins");
                if (Directory.Exists(skinsCache))
                    Directory.Delete(skinsCache, true);

                // yoriiskinsloader keeps a second cache here, wipe it too so every launch hits the worker for the latest skin
                var cslCache = Path.Combine(minecraftPath, "CustomSkinLoader", "caches");
                if (Directory.Exists(cslCache))
                    Directory.Delete(cslCache, true);
            }
            catch (Exception ex)
            {
                // dont fail launch if cache wipe fails, just log it
                Logger.Warn($"failed to clear skins cache: {ex.Message}");
            }
        }

        public async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            // prevent double click from launching two processes
            playButton.IsEnabled = false;

            try
            {
                var account = GetSelectedPlayerAccount();

                if (account == null || string.IsNullOrWhiteSpace(account.Username))
                {
                    NotificationHelper.Show("No player selected", "Choose or add a player before launching.");
                    playButton.IsEnabled = true;
                    return;
                }

                string username = account.Username;

                DownloadItem? installItem = null;

                bool hasInternet = await NetworkHelper.InternetAvailable();

                // refresh the active path so launching uses the currently selected instance folder
                minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();
                if (string.IsNullOrEmpty(minecraftPath))
                    minecraftPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

                var path = new MinecraftPath(minecraftPath);
                MinecraftLauncher launcher;

                // check for internet connectivity before launching to decide whether to attempt online login and online version loading or not
                if (hasInternet)
                    launcher = new MinecraftLauncher(path);
                else
                {
                    // only loads local versions and does not tries loading from the internet
                    var parameters = MinecraftLauncherParameters.CreateDefault(path);
                    parameters.VersionLoader = new LocalJsonVersionLoader(path);
                    launcher = new MinecraftLauncher(parameters);
                }

                // setting states for the progress bar
                // downloadprogressbar.opacity = 1;
                // downloadprogressbar.value = 0;
                // downloadprogressbar.isindeterminate = true;
                // downloadprogressvalue = 0;

                var instancesEnabled = SettingsManager.Current.InstancesEnabled;
                var selectedInstance = InstanceManager.GetSelectedInstance();

                if (instancesEnabled && selectedInstance == null)
                {
                    NotificationHelper.Show("No instance selected", "Create or select an instance on the Home page first.");

                    if (mainFrame.CurrentSourcePageType != typeof(HomePage))
                        mainFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());

                    playButton.IsEnabled = true;
                    return;
                }

                // update the progress
                launcher.FileProgressChanged += (s, args) =>
                {
                    if (App.IsShuttingDown) return;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        // if (args.totaltasks > 0)
                        // setdownloadprogress((double)args.progressedtasks / args.totaltasks * 100);
                    });
                };

                string? selectedVersion = versionComboBox.SelectedItem?.ToString();

                if (string.IsNullOrWhiteSpace(selectedVersion))
                {
                    NotificationHelper.Show("No version selected", "Select or install a Minecraft version before launching.");

                    playButton.IsEnabled = true;
                    return;
                }

                // vanilla installs create their download item only once real
                // bytes start moving (see byteprogresschanged below)
                bool lazyInstall = false;
                CancellationTokenSource? vanillaCts = null;

                launcher.ByteProgressChanged += (s, args) =>
                {
                    if (App.IsShuttingDown) return;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        // vanilla: the flyout item is created only here, when
                        // bytes actually move, so verification-only launches
                        // never show up as a download
                        if (lazyInstall && installItem == null)
                        {
                            installItem = DownloadManager.Add(selectedVersion, DownloadKind.Minecraft);
                            installItem.Token.Register(() => vanillaCts?.Cancel());
                            playButton.Content = "Downloading...";
                        }
                        installItem?.SetByteProgress(args.ProgressedBytes, args.TotalBytes);
                    });
                };

                bool isFabric = selectedVersion.StartsWith("Fabric ");
                bool isForge = selectedVersion.StartsWith("Forge ");
                bool isNeoForge = selectedVersion.StartsWith("NeoForge ");
                // bool isoptifine = selectedversion.startswith("optifine ");
                string baseVersion = isFabric
                    ? selectedVersion["Fabric ".Length..].Trim()
                    : isForge
                        ? selectedVersion["Forge ".Length..].Trim()
                        : isNeoForge
                            ? selectedVersion["NeoForge ".Length..].Trim()
                            : selectedVersion;

                string versionToLaunch;

                playButton.IsEnabled = false;
                playButton.Content = "Checking files...";

                // check if current version is fabric
                if (isFabric)
                {
                    if (hasInternet)
                    {
                        // download item is created lazily when bytes actually move
                        var fabricInstaller = new FabricInstaller(HttpService.Client);
                        lazyInstall = true;
                        vanillaCts = new CancellationTokenSource();
                        versionToLaunch = await fabricInstaller.Install(baseVersion, path);
                        await launcher.InstallAsync(versionToLaunch, vanillaCts.Token);
                    }
                    else
                    {
                        Logger.Info("Offline: skipping Fabric install");
                        versionToLaunch = $"Fabric {baseVersion}";
                    }
                }
                else if (isForge)
                {
                    if (hasInternet)
                    {
                        var forgeInstaller = new ForgeInstaller(launcher);
                        lazyInstall = true;
                        vanillaCts = new CancellationTokenSource();
                        versionToLaunch = await forgeInstaller.Install(baseVersion, new ForgeInstallOptions());
                        await launcher.InstallAsync(versionToLaunch, vanillaCts.Token);
                    }
                    else
                    {
                        Logger.Info("Offline: skipping Forge install");
                        versionToLaunch = $"Forge {baseVersion}";
                    }
                }
                else if (isNeoForge)
                {
                    if (hasInternet)
                    {
                        var neoForgeInstaller = new NeoForgeInstaller(launcher);
                        lazyInstall = true;
                        vanillaCts = new CancellationTokenSource();
                        versionToLaunch = await neoForgeInstaller.Install(baseVersion, new NeoForgeInstallOptions());
                        await launcher.InstallAsync(versionToLaunch, vanillaCts.Token);
                    }
                    else
                    {
                        Logger.Info("Offline: skipping NeoForge install");
                        versionToLaunch = $"NeoForge {baseVersion}";
                    }
                }
                else
                {
                    if (hasInternet)
                    {
                        // the download item is created lazily in the progress
                        // handler, so file verification on every launch doesnt
                        // show up as a download in the flyout
                        lazyInstall = true;
                        vanillaCts = new CancellationTokenSource();
                        await launcher.InstallAsync(selectedVersion, vanillaCts.Token);
                    }
                    else
                    {
                        Logger.Info("Offline: skipping vanilla install");
                    }

                    versionToLaunch = selectedVersion;
                }

                installItem?.Complete();

                playButton.Content = "Launching...";
                // downloadprogressbar.isindeterminate = false;
                // downloadprogressbar.value = 100;
                // downloadprogressvalue = 100;

                // set default ram amount to 4 gb
                double ramGb = SettingsManager.Current.RamAmount;

                // convert gb to mb for the launch arguments
                int ramMb = (int)(ramGb * 1024);

                LoginHelper.LoginResult loginResult;
                bool isMojangAccount = account.AccountType == PlayerAccountType.Mojang && !string.IsNullOrEmpty(account.MojangIdentifier);
                bool isOfflineAccount = account.AccountType == PlayerAccountType.Offline;
                bool isYoriiSkins = account.AccountType == PlayerAccountType.YoriiSkins;
                string yoriiUuid = isYoriiSkins
                    ? (!string.IsNullOrEmpty(account.CustomUUID) ? account.CustomUUID : Guid.NewGuid().ToString("N"))
                    : "";

                // microsoft account
                if (isMojangAccount)
                {
                    var session = await LoginHelper.LoginWithMojangSilently(account.MojangIdentifier!);
                    loginResult = new LoginHelper.LoginResult
                    {
                        Session = session,
                        IsOffline = false
                    };
                }
                else if (isOfflineAccount)
                {
                    loginResult = new LoginHelper.LoginResult
                    {
                        Session = LoginHelper.CreateOfflineSession(username),
                        IsOffline = true
                    };
                }
                else if (isYoriiSkins)
                {
                    loginResult = new LoginHelper.LoginResult
                    {
                        Session = new MSession
                        {
                            Username = username,
                            UUID = yoriiUuid,
                            AccessToken = Guid.NewGuid().ToString("N"),
                            UserType = "legacy"
                        },
                        IsOffline = true
                    };
                }
                else
                {
                    // defensive: unknown/legacy types launch as an offline player
                    loginResult = new LoginHelper.LoginResult
                    {
                        Session = LoginHelper.CreateOfflineSession(username),
                        IsOffline = true
                    };
                }

                List<MArgument> jvmArgs = [];

                // cloudflare's bot protection on the workers.dev domain returns 403
                // for the jvm's default "java/1.8.x" user-agent, which blocks
                // authlib-injector's metadata fetch and the game's skin downloads
                // from the yorii worker. override the ua so all requests pass
                jvmArgs.Add(new MArgument("-Dhttp.agent=YoriiLauncher/1.0"));

                if (isMojangAccount)
                {
                    Logger.Info("Mojang account: launching without authlib-injector");
                }
                else if (isOfflineAccount)
                {
                    Logger.Info("Offline account: launching without authlib-injector");
                }
                else if (isYoriiSkins)
                {
                    // yoriiskinsloader is a fork of customskinloader optimized for faster skin loading and other improvements
                    if (!InstanceManager.IsYoriiSkinsLoaderSupported(selectedVersion))
                    {
                        string injectorPath = EnsureAuthlibInjector();
                        Logger.Info($"Yorii Skins: launching with worker for {username}");
                        jvmArgs.Add(new MArgument($"-javaagent:{injectorPath}=https://yorii-worker.yoriiskin.workers.dev/"));
                    }
                    else
                    {
                        Logger.Info("Yorii Skins: mod instance, mod handles skins; skipping authlib-injector");
                    }
                }

                // w server list and world list
                var selectedServerAddress = SettingsManager.Current.ServerListEnabled
                    ? ServerManager.GetSelectedServerAddress()
                    : null;
                var selectedWorldId = SettingsManager.Current.WorldListEnabled
                    ? WorldManager.GetSelectedWorldId()
                    : null;

                // begin building the launch options
                var launchOption = new MLaunchOption
                {
                    MaximumRamMb = ramMb,
                    Session = loginResult.Session,
                    ExtraJvmArguments = jvmArgs.ToArray()
                };

                if (!string.IsNullOrWhiteSpace(selectedWorldId))
                {
                    launchOption.QuickPlaySingleplayer = selectedWorldId;
                }
                else if (!string.IsNullOrWhiteSpace(selectedServerAddress))
                {
                    var serverHost = selectedServerAddress;
                    var serverPort = 25565;

                    // split ip and port from servers.dat, port only included if non-default
                    var colonIdx = selectedServerAddress.LastIndexOf(':');
                    if (colonIdx > 0 &&
                        int.TryParse(selectedServerAddress[(colonIdx + 1)..], out var parsedPort) &&
                        parsedPort > 0 &&
                        parsedPort <= 65535)
                    {
                        serverHost = selectedServerAddress[..colonIdx];
                        serverPort = parsedPort;
                    }

                    // set autojoin target server
                    launchOption.ServerIp = serverHost;
                    launchOption.ServerPort = serverPort;
                }

                // yorii skins is our cloudflare auth server worker which fetches skins from github repo
                // wipe caches first so preload writes fresh files that survive — this gives latest skins for everyone else on next join
                ClearAssetsSkinsCache(minecraftPath);

                if (isYoriiSkins)
                {
                    await SkinManager.PreloadSkinForLaunchAsync(username, yoriiUuid);
                    // sync the other instances in the background so they dont
                    // launch with an old skin
                    _ = SkinManager.SyncSkinToAllInstancesAsync(username);
                }

                // yoriiskinsloader is a fork of customskinloader optimized for faster skin loading and other improvements
                InstanceManager.EnsureYoriiSkinsLoaderInstalled();

                var process = await launcher.BuildProcessAsync(versionToLaunch, launchOption);

                // read behavior first so we can configure the process accordingly
                string behavior = SettingsManager.Current.WindowBehavior;

                bool closeAfterLaunch = behavior == "Close";

                // when closing after launch, start the game as an independent process
                // so it survives the launcher exiting
                process.StartInfo.UseShellExecute = closeAfterLaunch;
                process.StartInfo.RedirectStandardOutput = !closeAfterLaunch;
                process.StartInfo.RedirectStandardError = !closeAfterLaunch;

                // empty working set before starting the game since the launcher is no longer needed
                MemoryOptimizer.ReduceMemory();
                process.Start();

                // hide progress bar now that the game has launched
                // downloadprogressbar.opacity = 0;
                // downloadprogressbar.isindeterminate = false;

                // set up console (not available in close mode since streams aren't redirected)
                bool showConsole = SettingsManager.Current.ShowConsole && !closeAfterLaunch;

                Console? console = null;
                if (showConsole)
                {
                    console = new Console();
                    console.Clear();
                    console.AppendLine($"[{DateTime.Now:HH:mm:ss}] Launching {versionToLaunch}...");
                    console.AppendLine($"[{DateTime.Now:HH:mm:ss}] ---");
                    console.Activate();
                }

                // set up launcher window behavior
                switch (behavior)
                {
                    case "Hide":
                        AppWindow.Hide();
                        break;
                    case "Close":
                        this.Close();
                        break;
                }

                // save the latest played instance so what it appears first in the instances section
                if (selectedInstance != null)
                    InstanceManager.MarkPlayed(selectedInstance.Id);

                // monitor the game process (only when streams are redirected)
                if (!closeAfterLaunch)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using StreamReader stdout = process.StandardOutput;
                            using StreamReader stderr = process.StandardError;

                            var stdoutTask = Task.Run(async () =>
                            {
                                string? line;
                                while ((line = await stdout.ReadLineAsync()) != null)
                                {
                                    Debug.WriteLine($"{line}");
                                    console?.AppendLine(line);
                                }
                            });

                            var stderrTask = Task.Run(async () =>
                            {
                                string? line;
                                while ((line = await stderr.ReadLineAsync()) != null)
                                {
                                    Debug.WriteLine($"{line}");
                                    console?.AppendLine($"{line}");
                                }
                            });

                            // wait for both output tasks to complete which will happen when the game exits
                            await Task.WhenAll(stdoutTask, stderrTask);
                        }
                        catch (IOException ex) when (ex.HResult == unchecked((int)0x800703E3) || ex.Message.Contains("aborted", StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Warn("Game process ended (piped output aborted)");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Launcher error in game process handler: {ex.Message}");
                        }
                        finally
                        {
                            await process.WaitForExitAsync();

                            if (!App.IsShuttingDown)
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    if (App.IsShuttingDown) return;
                                    playButton.Content = "Play";
                                    playButton.IsEnabled = true;
                                    // show window if hidden
                                    AppWindow.Show();
                                    // downloadprogressbar.opacity = 0;
                                    // downloadprogressbar.isindeterminate = false;
                                    MemoryOptimizer.ReduceMemory();
                                });
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // user pressed cancel on the install download mid-flight
                playButton.Content = "Play";
                playButton.IsEnabled = true;
                // downloadprogressbar.opacity = 0;
                // downloadprogressbar.isindeterminate = false;
            }
            catch (Exception ex)
            {
                // the real error is inside the inner exception so walk the chain to get it
                var root = ex;
                while (root.InnerException != null) root = root.InnerException;
                Logger.Error($"Launch failed: {ex.GetType().Name}: {ex.Message} | root: {root.GetType().Name}: {root.Message}");
                if (ex is Quiescent.Core.Version.VersionParseException)
                    Logger.Error($"  (version being launched: {InstanceManager.GetSelectedInstanceVersion()})");
                playButton.Content = "Play";
                playButton.IsEnabled = true;
                // downloadprogressbar.opacity = 0;
                // downloadprogressbar.isindeterminate = false;

                // check if it was network issue
                var isNetwork = ex is System.Net.Http.HttpRequestException
                    or System.Net.Sockets.SocketException
                    or TaskCanceledException;

                ShowNotification("Launch failed", isNetwork
                        ? "Could not connect to the internet. Check your connection and try again."
                        : $"An error occurred: {ex.Message}");
            }
        }

        public void AccountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ensure selected item is a valid account
            if (accountComboBox.SelectedItem is not AccountComboItem item)
                return;

            // manage players is selected
            if (item.IsManagePlayers)
            {
                accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => !x.IsAddNew && !x.IsManagePlayers);
                _ = ShowManagePlayersDialogAsync();
                return;
            }

            // add player is selected
            if (item.IsAddNew)
            {
                accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => !x.IsAddNew && !x.IsManagePlayers);
                _ = ShowAddPlayerDialogAsync();
                return;
            }

            // player account is selected
            if (item.Account != null)
                AccountManager.SetSelectedAccount(item.Account.Id);
        }

        private PlayerAccount? GetSelectedPlayerAccount()
        {
            // get the current player, fall back to saved account if add player or manage players is selected
            if (accountComboBox.SelectedItem is AccountComboItem { IsAddNew: false } item)
                return item.Account;

            return AccountManager.GetSelectedAccount();
        }

        private async Task ShowAddPlayerDialogAsync()
        {
            // create the elements in the dialog
            var usernameBox = new TextBox
            {
                Header = "Username",
                PlaceholderText = "Player name"
            };

            var accountTypeBox = new ComboBox
            {
                Header = "Account type",
                SelectedIndex = 0
            };

            accountTypeBox.Items.Add(new ComboBoxItem { Content = "Yorii Skins", Tag = PlayerAccountType.YoriiSkins });
            accountTypeBox.Items.Add(new ComboBoxItem { Content = "Mojang (Microsoft)", Tag = PlayerAccountType.Mojang });
            accountTypeBox.Items.Add(new ComboBoxItem { Content = "Offline", Tag = PlayerAccountType.Offline });
            if (Application.Current.Resources.TryGetValue("AcrylicComboBoxStyle", out object resource) && resource is Style acrylicStyle)
            {
                accountTypeBox.Style = acrylicStyle;
            }

            var hintText = new TextBlock
            {
                Text = "Yorii Skins profiles sync their skin through GitHub. Upload a skin on the Skins page.",
                FontSize = 12,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap
            };

            var panel = new StackPanel { Spacing = 10 };

            panel.Children.Add(accountTypeBox);
            panel.Children.Add(usernameBox);
            panel.Children.Add(hintText);

            // update field visibility based on selected account type
            accountTypeBox.SelectionChanged += (_, _) =>
            {
                bool isMojang = accountTypeBox.SelectedItem is ComboBoxItem item && item.Tag is PlayerAccountType.Mojang;
                bool isYoriiSkins = accountTypeBox.SelectedItem is ComboBoxItem ysItem && ysItem.Tag is PlayerAccountType.YoriiSkins;
                usernameBox.Visibility = isMojang ? Visibility.Collapsed : Visibility.Visible;
                hintText.Text = isMojang
                    ? "You'll be signed in via Microsoft OAuth."
                    : isYoriiSkins
                        ? "Yorii Skins profiles sync their skin through GitHub. Upload a skin on the Skins page."
                        : "Offline players can join any server, but skins only work when the Yorii Skins skin mod is installed.";
            };

            // get theme to apply to the dialog
            ElementTheme theme = ThemeHelper.GetCurrentTheme();

            // create the dialog
            var dialog = new ContentDialog
            {
                Title = "Add player",
                Content = panel,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = rootGrid.XamlRoot,
                Background = DialogHelper.GetAcrylicBrush(),
                RequestedTheme = theme
            };

            // show the dialog and wait for the result
            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();

            if (result != ContentDialogResult.Primary)
                return;

            // ensure selected item is a valid account type
            if (accountTypeBox.SelectedItem is not ComboBoxItem selectedTypeItem ||
                selectedTypeItem.Tag is not PlayerAccountType accountType)
            {
                accountType = PlayerAccountType.YoriiSkins;
            }

            if (accountType == PlayerAccountType.Mojang)
            {
                try
                {
                    playButton.IsEnabled = false;
                    playButton.Content = "Signing in...";

                    // launch window for microsoft login
                    var (session, identifier) = await LoginHelper.LoginWithMojangInteractive();

                    var account = new PlayerAccount
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Username = session.Username,
                        Password = null,
                        AccountType = PlayerAccountType.Mojang,
                        MojangIdentifier = identifier
                    };

                    AccountManager.SaveAccount(account);
                    LoadAccounts();
                    accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => x.Account?.Id == account.Id);

                    ShowNotification("Account added", $"Signed in as {session.Username}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Mojang login failed: {ex.Message}");
                    ShowNotification("Login failed", ex.Message);
                }
                finally
                {
                    playButton.Content = "Play";
                    playButton.IsEnabled = true;
                }
                return;
            }

            var username = usernameBox.Text.Trim();
            // check if username is empty
            if (string.IsNullOrWhiteSpace(username))
            {
                ShowNotification("Username is empty", "Enter a player name before adding the account.");
                return;
            }

            PlayerAccount newAccount;
            if (accountType == PlayerAccountType.Offline)
            {
                newAccount = new PlayerAccount
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Username = username,
                    Password = null,
                    AccountType = PlayerAccountType.Offline
                };
            }
            else
            {
                // yorii skins is our cloudflare auth server worker which fetches skins from github repo
                newAccount = new PlayerAccount
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Username = username,
                    Password = null,
                    AccountType = PlayerAccountType.YoriiSkins,
                    CustomUUID = Guid.NewGuid().ToString("N")
                };
            }

            AccountManager.SaveAccount(newAccount);
            LoadAccounts();
            accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => x.Account?.Id == newAccount.Id);

            ShowNotification("Account added", newAccount.AccountType == PlayerAccountType.Offline
                ? $"{username} added as offline player."
                : $"{username} added as Yorii Skins player.");
        }

        private async Task ShowManagePlayersDialogAsync()
        {
            // load accounts and theme
            var accounts = AccountManager.LoadAccounts();
            var theme = ThemeHelper.GetCurrentTheme();

            var scrollViewer = new ScrollView
            {
                VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Auto,
                Width = 260,
                MaxHeight = 420
            };

            // set main stackpanel
            var itemsPanel = new StackPanel { Spacing = 0 };

            foreach (var account in accounts)
            {
                var nameBlock = new TextBlock
                {
                    Text = account.Username,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var typeBlock = new TextBlock
                {
                    Text = PlayerAccount.GetAccountTypeLabel(account.AccountType),
                    FontSize = 12,
                    Opacity = 0.7,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var textStack = new StackPanel
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center
                };
                textStack.Children.Add(nameBlock);
                textStack.Children.Add(typeBlock);

                var editButton = new Button
                {
                    Width = 32,
                    Height = 32,
                    Padding = new Thickness(0),
                    Tag = account,
                };
                ToolTipService.SetToolTip(editButton, "Edit player");
                editButton.BorderThickness = new Thickness(0);
                editButton.Background = new SolidColorBrush(Colors.Transparent);
                editButton.Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 };
                editButton.Click += ManagePlayer_Edit_Click;

                var deleteButton = new Button
                {
                    Width = 32,
                    Height = 32,
                    Padding = new Thickness(0),
                    Tag = account
                };
                ToolTipService.SetToolTip(deleteButton, "Delete player");
                deleteButton.BorderThickness = new Thickness(0);
                deleteButton.Background = new SolidColorBrush(Colors.Transparent);
                deleteButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 };
                deleteButton.Click += ManagePlayer_Delete_Click;

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                buttonsPanel.Children.Add(editButton);
                buttonsPanel.Children.Add(deleteButton);

                var rowGrid = new Grid
                {
                    Padding = new Thickness(12, 10, 12, 10)
                };

                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(textStack, 0);
                Grid.SetColumn(buttonsPanel, 1);
                rowGrid.Children.Add(textStack);
                rowGrid.Children.Add(buttonsPanel);

                var rowBorder = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 0, 4),
                    Child = rowGrid
                };

                itemsPanel.Children.Add(rowBorder);
            }

            // if no accounts
            if (accounts.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "No players added yet.",
                    FontSize = 12,
                    Opacity = 0.7,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 24)
                };
                itemsPanel.Children.Add(emptyText);
            }

            scrollViewer.Content = itemsPanel;

            // create dialog
            managePlayersDialog = new ContentDialog
            {
                Title = "Manage players",
                Content = scrollViewer,
                CloseButtonText = "Close",
                XamlRoot = rootGrid.XamlRoot,
                RequestedTheme = theme,
                Background = DialogHelper.GetAcrylicBrush()
            };

            // show dialog
            managePlayersDialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            await managePlayersDialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();

            managePlayersDialog = null;
            LoadAccounts();
        }

        private async void ManagePlayer_Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PlayerAccount account)
            {
                var dialog = managePlayersDialog;
                managePlayersDialog = null;
                dialog?.Hide();

                // show edit dialog for the selected account
                await ShowEditPlayerDialogAsync(account);
            }
        }

        private async void ManagePlayer_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PlayerAccount account)
            {
                var manageDialog = managePlayersDialog;
                managePlayersDialog = null;
                manageDialog?.Hide();

                // confirm deletion
                var confirmDialog = new ContentDialog
                {
                    Title = "Delete player",
                    Content = $"Delete {account.Username}?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    // destructive dialogs default to cancel so enter doesnt delete
                    DefaultButton = ContentDialogButton.Close,
                    Background = DialogHelper.GetAcrylicBrush(),
                    XamlRoot = rootGrid.XamlRoot,
                    RequestedTheme = ThemeHelper.GetCurrentTheme()
                };

                confirmDialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
                var result = await confirmDialog.ShowAsync();
                MemoryOptimizer.ReduceMemory();

                if (result != ContentDialogResult.Primary)
                {
                    await ShowManagePlayersDialogAsync();
                    return;
                }

                // delete the account
                AccountManager.DeleteAccount(account.Id);

                if (!string.IsNullOrEmpty(account.MojangIdentifier))
                    _ = LoginHelper.RemoveMojangAccount(account.MojangIdentifier);

                // refresh
                LoadAccounts();

                await ShowManagePlayersDialogAsync();
            }
        }

        private async Task ShowEditPlayerDialogAsync(PlayerAccount account)
        {
            bool isMojang = account.AccountType == PlayerAccountType.Mojang; // / check if mojang account

            var usernameBox = new TextBox
            {
                Header = "Username",
                Text = account.Username,
                IsReadOnly = isMojang
            };

            var accountTypeBox = new ComboBox
            {
                Header = "Account type"
            };

            accountTypeBox.Items.Add(new ComboBoxItem { Content = "Yorii Skins", Tag = PlayerAccountType.YoriiSkins });
            accountTypeBox.Items.Add(new ComboBoxItem { Content = "Mojang (Microsoft)", Tag = PlayerAccountType.Mojang });
            accountTypeBox.Items.Add(new ComboBoxItem { Content = "Offline", Tag = PlayerAccountType.Offline });
            if (Application.Current.Resources.TryGetValue("AcrylicComboBoxStyle", out object resource) && resource is Style acrylicStyle)
            {
                accountTypeBox.Style = acrylicStyle;
            }

            for (int i = 0; i < accountTypeBox.Items.Count; i++)
            {
                if (accountTypeBox.Items[i] is ComboBoxItem item && item.Tag is PlayerAccountType type && type == account.AccountType)
                {
                    accountTypeBox.SelectedIndex = i;
                    break;
                }
            }

            if (accountTypeBox.SelectedIndex < 0)
                accountTypeBox.SelectedIndex = 0;

            var hintText = new TextBlock
            {
                Text = isMojang
                    ? "Microsoft accounts are authenticated via OAuth. Click Save to re-authenticate."
                    : "Yorii Skins profiles sync their skin through GitHub. Upload a skin on the Skins page.",
                FontSize = 12,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap
            };

            // update field visibility based on selected account type
            accountTypeBox.SelectionChanged += (_, _) =>
            {
                bool nowMojang = accountTypeBox.SelectedItem is ComboBoxItem selItem && selItem.Tag is PlayerAccountType.Mojang;
                bool nowYoriiSkins = accountTypeBox.SelectedItem is ComboBoxItem ysItem && ysItem.Tag is PlayerAccountType.YoriiSkins;
                usernameBox.IsReadOnly = nowMojang;
                hintText.Text = nowMojang
                    ? "Microsoft accounts are authenticated via OAuth. Click Save to re-authenticate."
                    : nowYoriiSkins
                        ? "Yorii Skins profiles sync their skin through GitHub. Upload a skin on the Skins page."
                        : "Offline players can join any server, but skins only work when the Yorii Skins skin mod is installed.";
            };

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(accountTypeBox);
            panel.Children.Add(usernameBox);
            panel.Children.Add(hintText);

            // create dialog
            var dialog = new ContentDialog
            {
                Title = "Edit player",
                Content = panel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = rootGrid.XamlRoot,
                Background = DialogHelper.GetAcrylicBrush(),
                RequestedTheme = ThemeHelper.GetCurrentTheme()
            };

            // show dialog
            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var dialogResult = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();

            if (dialogResult != ContentDialogResult.Primary)
                return;

            if (accountTypeBox.SelectedItem is ComboBoxItem selectedTypeItem &&
                selectedTypeItem.Tag is PlayerAccountType newAccountType)
            {
                if (newAccountType == PlayerAccountType.Mojang)
                {
                    if (account.AccountType != PlayerAccountType.Mojang)
                    {
                        try
                        {
                            playButton.IsEnabled = false;
                            playButton.Content = "Signing in...";

                            var (session, identifier) = await LoginHelper.LoginWithMojangInteractive();

                            account.Username = session.Username;
                            account.Password = null;
                            account.AccountType = PlayerAccountType.Mojang;
                            account.MojangIdentifier = identifier;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Mojang login failed: {ex.Message}");
                            ShowNotification("Login failed", ex.Message);
                            return;
                        }
                        finally
                        {
                            playButton.Content = "Play";
                            playButton.IsEnabled = true;
                        }
                    }
                    else
                    {
                        try
                        {
                            playButton.IsEnabled = false;
                            playButton.Content = "Signing in...";

                            var (session, identifier) = await LoginHelper.LoginWithMojangInteractive();

                            account.Username = session.Username;
                            account.MojangIdentifier = identifier;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Mojang re-auth failed: {ex.Message}");
                            ShowNotification("Re-authentication failed", ex.Message);
                            return;
                        }
                        finally
                        {
                            playButton.Content = "Play";
                            playButton.IsEnabled = true;
                        }
                    }
                }
                else if (newAccountType == PlayerAccountType.Offline)
                {
                    account.Username = usernameBox.Text.Trim();
                    account.Password = null;
                    account.AccountType = PlayerAccountType.Offline;
                    account.MojangIdentifier = null;
                    account.CustomUUID = null;
                }
                else if (newAccountType == PlayerAccountType.YoriiSkins)
                {
                    account.Username = usernameBox.Text.Trim();
                    account.Password = null;
                    account.AccountType = PlayerAccountType.YoriiSkins;
                    account.MojangIdentifier = null;
                    account.CustomUUID ??= Guid.NewGuid().ToString("N");
                }
                else
                {
                    // unknown/removed types are treated as offline players
                    account.Username = usernameBox.Text.Trim();
                    account.Password = null;
                    account.AccountType = PlayerAccountType.Offline;
                    account.MojangIdentifier = null;
                }
            }

            // refresh accounts
            AccountManager.UpdateAccount(account);
            LoadAccounts();
            accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => x.Account?.Id == account.Id);
        }

        private static void ShowNotification(string title, string message)
        {
            NotificationHelper.Show(title, message);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // check if current page is not settingspage, if yes then navigate to settings page
            if (mainFrame.CurrentSourcePageType != typeof(SettingsPage))
                mainFrame.Navigate(typeof(SettingsPage), null, new SuppressNavigationTransitionInfo());
        }

        private void titleBar_BackRequested(TitleBar sender, object args)
        {
            if (mainFrame.CanGoBack)
            {
                mainFrame.GoBack();
                MemoryOptimizer.ReduceMemory();
            }
        }

        // private void setdownloadprogress(double progress)
        // {
        // // set indeterminate when progress is 0 to indicate something is happening
        // downloadprogressbar.isindeterminate = false;

        // if (progress < downloadprogressvalue)
        // return;

        // downloadprogressvalue = math.min(progress, 100);
        // downloadprogressbar.value = downloadprogressvalue;
        // }

        // keeps the title-bar downloads flyout in sync with any active downloads
        private void UpdateDownloadsIndicator()
        {
            bool active = DownloadManager.HasActiveDownloads;
            downloadsProgressRing.IsActive = active;
            downloadsProgressRing.Opacity = active ? 1 : 0;

            bool any = DownloadManager.Items.Count > 0;
            downloadsHeaderText.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            downloadsEmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            downloadsListView.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is DownloadItem item)
                item.Cancel();
        }

        private void SetWindowIcon()
        {
            // setting window icon in taskbar thumbnail, alt-tab and task manager
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "128.ico");
                if (File.Exists(iconPath))
                    AppWindow.SetIcon(iconPath);
            }
            catch
            {
                Logger.Warn("Failed to set window icon.");
            }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            var vm = VersionVM;
            SettingsManager.Current.ShowSnapshots = vm.ShowSnapshots;
            SettingsManager.Current.ShowFabric = vm.ShowFabric;
            SettingsManager.Current.ShowForge = vm.ShowForge;
            SettingsManager.Current.ShowNeoForge = vm.ShowNeoForge;
            // settingsmanager.current.showoptifine = vm.showoptifine;
            SettingsManager.Current.ShowOld = vm.ShowOld;
            SettingsManager.SaveSettings();
        }

        private void ModsButton_Click(object sender, RoutedEventArgs e)
        {
            // check if no instances are selected
            if (SettingsManager.Current.InstancesEnabled && InstanceManager.GetSelectedInstance() == null)
            {
                return;
            }

            // check if current page is not modspage, if yes then navigate to modspage
            if (mainFrame.CurrentSourcePageType != typeof(ModsPage))
            {
                mainFrame.Navigate(typeof(ModsPage), null, new SuppressNavigationTransitionInfo());
                MemoryOptimizer.ReduceMemory();
            }
        }

        private async void BugReportButton_Click(object sender, RoutedEventArgs e)
        {
            // open github issues page to create a new issue
            var uri = new Uri("https://github.com/yoriichi111012/Yorii-Launcher/issues/new");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }

        // keeps the titlebar account button in sync with the github login
        // state: avatar + username when connected, generic icon when not
        public void UpdateGitHubAccountButton()
        {
            string? username = SettingsManager.Current.GitHubUsername;
            bool loggedIn = SkinManager.IsLoggedIn && !string.IsNullOrEmpty(username);

            if (loggedIn)
            {
                // keep the button at its xaml size of 32x32 - shrinking it to
                // 24x24 leaves less content space than the 24x24 avatar needs
                // once the button's 1px border is subtracted, clipping the
                // bottom edge of the image
                // also kill the hover/pressed plate so the avatar sits on
                // transparent like the rest of the titlebar
                AccountsButton.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Colors.Transparent);
                AccountsButton.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Colors.Transparent);
                var avatar = new BitmapImage(new Uri($"https://avatars.githubusercontent.com/{username}?size=64"));
                githubAvatarButtonImage.Fill = new ImageBrush { ImageSource = avatar, Stretch = Stretch.UniformToFill };
                githubAvatarImage.Fill = new ImageBrush { ImageSource = avatar, Stretch = Stretch.UniformToFill };
                githubAvatarButtonImage.Visibility = Visibility.Visible;
                genericAccountButtonIcon.Visibility = Visibility.Collapsed;
                githubAvatarImage.Visibility = Visibility.Visible;
                genericAccountIcon.Visibility = Visibility.Collapsed;
                accountNameText.Text = username;
                accountStatusText.Text = "Connected with GitHub";
                viewGitHubProfileItem.Visibility = Visibility.Visible;
                disconnectItemText.Text = "Sign Out";
                ToolTipService.SetToolTip(AccountsButton, username);
            }
            else
            {
                // restore the default hover/pressed plate for the icon state
                AccountsButton.Resources.Remove("ButtonBackgroundPointerOver");
                AccountsButton.Resources.Remove("ButtonBackgroundPressed");
                githubAvatarButtonImage.Fill = null;
                githubAvatarImage.Fill = null;
                githubAvatarButtonImage.Visibility = Visibility.Collapsed;
                genericAccountButtonIcon.Visibility = Visibility.Visible;
                githubAvatarImage.Visibility = Visibility.Collapsed;
                genericAccountIcon.Visibility = Visibility.Visible;
                accountNameText.Text = "Not signed in";
                accountStatusText.Text = "Sign in to link private profiles";
                viewGitHubProfileItem.Visibility = Visibility.Collapsed;
                disconnectItemText.Text = "Sign In";
                ToolTipService.SetToolTip(AccountsButton, "Account");
            }
        }

        private void AccountFlyout_Skins_Click(object sender, RoutedEventArgs e)
        {
            accountsFlyout.Hide();
            NavigateToSkins();
        }

        private void AccountFlyout_Themes_Click(object sender, RoutedEventArgs e)
        {
            accountsFlyout.Hide();
            mainFrame.Navigate(typeof(Pages.ThemesPage));
        }

        private async void AccountFlyout_Profile_Click(object sender, RoutedEventArgs e)
        {
            accountsFlyout.Hide();

            // logged-in users land on their own private profiles repo; without a
            // github login there is no private repo, so fall back to the public index
            string owner = SkinManager.IsLoggedIn
                ? SettingsManager.Current.GitHubUsername ?? SkinManager.IndexRepoOwner
                : SkinManager.IndexRepoOwner;
            string repo = SkinManager.IsLoggedIn ? "yorii-profiles" : SkinManager.IndexRepo;

            await Windows.System.Launcher.LaunchUriAsync(new Uri($"https://github.com/{owner}/{repo}"));
        }

        private async void AccountFlyout_Disconnect_Click(object sender, RoutedEventArgs e)
        {
            accountsFlyout.Hide();

            if (SkinManager.IsLoggedIn)
            {
                // remove the logged-in user's yoriiskins accounts, then clear
                // the saved token so private profiles disappear everywhere
                var accounts = AccountManager.LoadAccounts();
                accounts.RemoveAll(a => a.AccountType == PlayerAccountType.YoriiSkins && a.GitHubOwner == SettingsManager.Current.GitHubUsername);
                AccountManager.SaveAll(accounts);
                SkinManager.Logout();
            }
            else
            {
                // sign in: same flow as the skins page login button
                try
                {
                    await SkinManager.AuthenticateWithGitHub();
                    try
                    {
                        await SkinManager.LoadProfilesIntoAccounts();
                    }
                    catch
                    {
                    }

                    // bring the launcher window back to the foreground now that
                    // the browser callback finished
                    var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    NativeMethods.ShowWindow(handle, 9); // sw_restore
                    NativeMethods.SetForegroundWindow(handle);

                    NotificationHelper.Show(
                        "Signed in",
                        $"Signed in as {SettingsManager.Current.GitHubUsername}. You can close the browser tab.",
                        silent: true);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Titlebar login failed: {ex.Message}");
                }
            }

            LoadAccounts();
            UpdateGitHubAccountButton();

            // if the skins page is open, refresh its profile list so private
            // profiles appear/disappear with the login state change
            if (mainFrame.Content is Pages.SkinsPage skinsPage)
                skinsPage.RefreshAfterAuthChange();
        }

        private void NotesButton_Click(object sender, RoutedEventArgs e)
        {
            // check if current page is not notespage, if yes then navigate to notespage
            if (mainFrame.CurrentSourcePageType != typeof(HomePage))
            {
                mainFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
                MemoryOptimizer.ReduceMemory();
            }
        }

        public void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (versionComboBox.SelectedItem != null)
            {
                var selectedVersion = versionComboBox.SelectedItem.ToString();

                if (string.IsNullOrWhiteSpace(selectedVersion))
                    return;

                // set version normally when instances disabled
                SettingsManager.Current.LastSavedVersion = selectedVersion;
                SettingsManager.Current.SelectedVersion = selectedVersion;

                // set version for particular instance
                if (SettingsManager.Current.InstancesEnabled)
                    InstanceManager.SetSelectedInstanceVersion(selectedVersion);

                SettingsManager.SaveSettings();
            }
        }

        public void VersionList_DropDownClosed(object sender, object e)
        {
            // version list can be really long so reduce memory won't hurt when it is closed
            MemoryOptimizer.ReduceMemory();
        }

        /*

        private List<string> searchCommands = new List<string>()
        {
            "Run",
        };

        private void searchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var suitableItems = new List<string>();
                var splitText = sender.Text.ToLower().Split(" ");
                foreach (var option in searchCommands)
                {
                    var found = splitText.All((key) =>
                    {
                        return option.ToLower().Contains(key);
                    });
                    if (found)
                    {
                        suitableItems.Add(option);
                    }
                }
                if (suitableItems.Count == 0)
                {
                    suitableItems.Add("No results found");
                }
                sender.ItemsSource = suitableItems;
            }

        }

        private void searchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion != null)
            {
                ExecuteSearch(args.ChosenSuggestion.ToString());
            }
            else
            {
                ExecuteSearch(args.QueryText);
            }
        }

        private void ExecuteSearch(string query)
        {
            // implementation for executing search
            if (query == "No results found") return;

            if (query == "Run")
            {
                
            }
        } */
    }
}