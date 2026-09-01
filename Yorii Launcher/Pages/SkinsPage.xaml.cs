using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using Yorii_Launcher;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Pages
{
    public sealed partial class SkinsPage : Page
    {
        private int loadGeneration;
        private CancellationTokenSource? loadCts;

        public SkinsPage()
        {
            this.InitializeComponent();
            UpdateUI();
            MemoryOptimizer.ReduceMemory();
        }

        private void UpdateUI()
        {
            // github login state lives in the titlebar account button; the
            // skins page only cares that public profiles need no login, so
            // adding is always allowed
            addProfileButton.IsEnabled = true;
            _ = LoadProfilesAsync();
        }

        // called by mainwindow when the github login state changes while the
        // page is already on screen, so private/public visibility updates
        // without needing a re-navigation
        public void RefreshAfterAuthChange()
        {
            _ = LoadProfilesAsync();
        }

        private async Task LoadProfilesAsync()
        {
            loadCts?.Cancel();
            var cts = loadCts = new CancellationTokenSource();
            var generation = ++loadGeneration;
            var token = cts.Token;
            Logger.Info($"LoadProfiles gen={generation} start");

            // 1) render the last known state instantly (local snapshot, no
            // network) so the page never sits on a skeleton waiting for the
            // raw cdn; the background refresh below catches up afterwards
            List<ProfileEntry> profiles;
            try
            {
                profiles = await SkinManager.GetProfilesAsync(token);
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"LoadProfiles gen={generation} cancelled");
                return;
            }
            catch
            {
                if (generation != loadGeneration || App.IsShuttingDown) return;
                skeletonPanel.Visibility = Visibility.Collapsed;
                profilesList.Visibility = Visibility.Collapsed;
                emptyPanel.Visibility = Visibility.Collapsed;
                errorPanel.Visibility = Visibility.Visible;
                Logger.Warn($"LoadProfiles gen={generation} error state shown");
                return;
            }

            Logger.Info($"LoadProfiles gen={generation} snapshot count={profiles.Count}");
            if (generation != loadGeneration || App.IsShuttingDown) return;
            _ = RenderProfilesAsync(profiles, generation, token);

            // 2) revalidate against github in the background; re-render only
            // when the index actually changed (no flicker on every visit)
            try
            {
                var fresh = await SkinManager.RefreshProfilesAsync(token);
                if (generation != loadGeneration || App.IsShuttingDown) return;
                bool changed = !ProfilesEqual(profiles, fresh);
                Logger.Info($"LoadProfiles gen={generation} refresh count={fresh.Count} changed={changed}");
                if (changed)
                    await RenderProfilesAsync(fresh, generation, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // offline — the local snapshot stays on screen
                Logger.Warn($"LoadProfiles gen={generation} refresh failed, keeping snapshot");
            }
        }

        private static void ApplySyncStatus(
            ProfileListItem item, SkinSyncInfo info, ref int syncedCount, ref int dirtyCount)
        {
            if (!info.HasLocal)
            {
                item.SyncStatus = "Published — not saved locally";
                item.SyncBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x8A, 0x8A, 0x8A));
            }
            else if (!info.RemoteReachable)
            {
                item.SyncStatus = "Local skin saved — sync unknown";
                item.SyncBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xC7, 0x9A, 0x00));
            }
            else if (info.MatchesRemote)
            {
                item.SyncStatus = "Synced to GitHub";
                item.SyncBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x6C, 0xCB, 0x5F));
                syncedCount++;
            }
            else
            {
                item.SyncStatus = "Local skin differs from published";
                item.SyncBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xFF, 0x78, 0x78));
                dirtyCount++;
            }
        }

        private void UpdateSyncStats(List<ProfileListItem> items)
        {
            int synced = 0, dirty = 0;
            foreach (var item in items)
            {
                if (item.SyncStatus == "Synced to GitHub") synced++;
                else if (item.SyncStatus == "Local skin differs from published") dirty++;
            }
            statsSyncedText.Text = synced.ToString();
            statsDirtyText.Text = dirty.ToString();
        }

        // re-checks sync for one profile until the remote skin becomes
        // reachable or the attempts run out; updates the row in place (inpc)
        private async Task RetrySyncCheckAsync(
            ProfileListItem item, List<ProfileListItem> items, int generation, CancellationToken token)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(3), token); }
                catch (OperationCanceledException) { return; }
                if (generation != loadGeneration || App.IsShuttingDown) return;

                SkinSyncInfo info;
                try
                {
                    info = await SkinManager.RecheckSyncAsync(item.Username, token);
                }
                catch (OperationCanceledException) { return; }
                catch { continue; }

                if (!info.RemoteReachable) continue;

                int synced = 0, dirty = 0;
                ApplySyncStatus(item, info, ref synced, ref dirty);
                UpdateSyncStats(items);
                return;
            }
        }

        private async Task RenderProfilesAsync(
            List<ProfileEntry> profiles, int generation, CancellationToken token)
        {
            // only profiles owned by this device/user are visible:
            // public ones this device claimed, and private ones belonging
            // to the logged-in github account
            var visible = profiles
                .Where(p => SettingsManager.Current.ClaimTokens.ContainsKey(p.Username)
                         || (SettingsManager.Current.GitHubUsername is not null && p.Owner == SettingsManager.Current.GitHubUsername))
                .ToList();
            var items = visible.Select(p => new ProfileListItem
            {
                Username = p.Username,
                CustomUUID = p.Uuid,
                SkinUrl = p.SkinUrl,
                Kind = p.Kind,
                KindLabel = p.Kind == "public" ? "Public" : "Private",
                ClaimLabel = p.Kind == "public" && SettingsManager.Current.ClaimTokens.ContainsKey(p.Username)
                    ? "- Claimed on this device"
                    : ""
            }).ToList();

            if (generation != loadGeneration || App.IsShuttingDown) return;

            // list + counts appear immediately; sync status and heads fill in
            // as their async checks complete
            profilesList.ItemsSource = items;
            Logger.Info($"RenderProfiles gen={generation} ItemsSource set, items={items.Count}");
            profilesList.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            emptyPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            skeletonPanel.Visibility = Visibility.Collapsed;
            errorPanel.Visibility = Visibility.Collapsed;
            statsCountText.Text = items.Count.ToString();
            statsSyncedText.Text = "—";
            statsDirtyText.Text = "—";
            profilesInfoText.Text = $"{items.Count} profile(s) loaded.";

            int syncedCount = 0;
            int dirtyCount = 0;

            // resolve sync status for all profiles in parallel; right after a
            // mutation the cache may hold a pre-propagation result, so bypass
            // it until github has caught up
            SkinSyncInfo[] syncInfos;
            try
            {
                syncInfos = await Task.WhenAll(items.Select(i =>
                    SkinManager.MutationPending
                        ? SkinManager.RecheckSyncAsync(i.Username, token)
                        : SkinManager.GetSyncInfoAsync(i.Username, token)));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                return;
            }
            if (generation != loadGeneration || App.IsShuttingDown) return;

            for (int i = 0; i < items.Count; i++)
            {
                var info = syncInfos[i];
                ApplySyncStatus(items[i], info, ref syncedCount, ref dirtyCount);
            }

            statsSyncedText.Text = syncedCount.ToString();
            statsDirtyText.Text = dirtyCount.ToString();

            // a freshly uploaded skin can take ~20s to propagate through the
            // github api before the worker proxy can serve it; re-check any
            // profile whose remote was unreachable until it resolves
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.SyncStatus == "Local skin saved — sync unknown")
                    _ = RetrySyncCheckAsync(item, items, generation, token);
            }

            // head previews last; each reads the local csl skin first (instant)
            // and falls back to the published skin when none is saved locally
            for (int i = 0; i < items.Count; i++)
            {
                if (generation != loadGeneration || App.IsShuttingDown) return;
                var item = items[i];
                try
                {
                    byte[]? skinBytes = await SkinManager.GetSkinBytesLocalFirstAsync(item.Username, item.SkinUrl, token);
                    if (skinBytes is not null && !App.IsShuttingDown)
                        item.PreviewImage = await SkinHeadRenderer.RenderHeadAsync(skinBytes);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // preview is optional - keep the row without an image
                }
            }
        }

        private static bool ProfilesEqual(List<ProfileEntry> a, List<ProfileEntry> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                var x = a[i];
                var y = b[i];
                if (x.Username != y.Username || x.Kind != y.Kind ||
                    x.Uuid != y.Uuid || x.SkinUrl != y.SkinUrl)
                    return false;
            }
            return true;
        }

        private async void AddProfileButton_Click(object sender, RoutedEventArgs e)
        {
            // anonymous users own at most one public profile (tracked by its
            // claim token); log in to publish private profiles instead
            if (!SkinManager.IsLoggedIn && SettingsManager.Current.ClaimTokens.Count >= 1)
            {
                ShowInfo("You already have one public profile. Sign in with GitHub to publish more (up to 5 private profiles).");
                return;
            }
            await ShowAddOrUpdateProfileDialogAsync(null);
        }

        private async void UpdateProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string username) return;
            await ShowAddOrUpdateProfileDialogAsync(username);
        }

        private async Task ShowAddOrUpdateProfileDialogAsync(string? prefillUsername)
        {
            var dialog = new ContentDialog
            {
                Title = prefillUsername == null ? "Add Skin Profile" : $"Update skin - {prefillUsername}",
                XamlRoot = this.XamlRoot,
                PrimaryButtonText = "Upload",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                Background = DialogHelper.GetAcrylicBrush(),
                RequestedTheme = ThemeHelper.GetCurrentTheme()
            };

            var usernameBox = new TextBox
            {
                Header = "Minecraft Username",
                PlaceholderText = "In game name",
                Margin = new Thickness(0, 0, 0, 8)
            };
            if (prefillUsername != null)
                usernameBox.Text = prefillUsername;

            var fileButton = new Button
            {
                Content = "Select Skin File (64x64 PNG)",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var fileText = new TextBlock
            {
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var previewImage = new Image
            {
                MaxHeight = 128,
                MaxWidth = 128,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };

            string? selectedFile = null;
            fileButton.Click += async (_, _) =>
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".png");
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance!);
                InitializeWithWindow.Initialize(picker, hwnd);
                var file = await picker.PickSingleFileAsync();
                if (App.IsShuttingDown) return;
                if (file != null)
                {
                    selectedFile = file.Path;
                    fileText.Text = $"Selected: {file.Name}";
                    try
                    {
                        using var stream = await file.OpenReadAsync();
                        var bmp = new BitmapImage();
                        await bmp.SetSourceAsync(stream);
                        previewImage.Source = bmp;
                        previewImage.Visibility = Visibility.Visible;
                    }
                    catch { }
                }
            };

            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(usernameBox);
            panel.Children.Add(fileButton);
            panel.Children.Add(fileText);
            panel.Children.Add(previewImage);

            // private profiles require a linked github account (limit: 5 per
            // account); public profiles are claim-token based and need no login
            var kindToggle = new ToggleSwitch
            {
                Header = "Private profile (GitHub)",
                OnContent = "Private — linked to your GitHub account",
                OffContent = "Public — visible to everyone, no login needed",
                IsOn = SkinManager.IsLoggedIn,
                IsEnabled = SkinManager.IsLoggedIn,
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(kindToggle);

            dialog.Content = panel;

            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();
            if (result != ContentDialogResult.Primary) return;

            string username = usernameBox.Text.Trim();
            if (string.IsNullOrEmpty(username) || selectedFile == null)
            {
                ShowInfo("Enter a username and select a skin file.");
                return;
            }

            string kind = kindToggle.IsOn ? "private" : "public";

            // guard the 5-private-profile limit client-side so the user gets a
            // clear message instead of a server 409 after picking a file
            if (kind == "private" && prefillUsername == null)
            {
                try
                {
                    var allProfiles = await SkinManager.GetProfilesAsync();
                    int privateCount = allProfiles.Count(p =>
                        p.Kind == "private" && p.Owner == SettingsManager.Current.GitHubUsername);
                    if (privateCount >= 5)
                    {
                        ShowInfo("You already have 5 private profiles (the maximum). Delete one or publish this skin as public instead.");
                        return;
                    }
                }
                catch
                {
                    // a stale snapshot must never block an upload; the server
                    // still enforces the limit if we're wrong
                }
            }

            SetBusy($"Uploading '{username}' as {(kind == "public" ? "public" : "private")}...");
            try
            {
                byte[] data = await File.ReadAllBytesAsync(selectedFile);

                string selectedHash = Convert.ToHexString(SHA256.HashData(data));
                string? currentHash = null;
                try
                {
                    var currentBytes = await HttpService.Client.GetByteArrayAsync($"{SkinManager.WorkerBaseUrl}/MinecraftSkins/{Uri.EscapeDataString(username)}.png");
                    currentHash = Convert.ToHexString(SHA256.HashData(currentBytes));
                }
                catch { }

                if (currentHash != null && string.Equals(selectedHash, currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    var confirm = new ContentDialog
                    {
                        Title = "Same skin?",
                        Content = $"'{username}' already has this exact skin published. Nothing will change. Upload anyway?",
                        XamlRoot = this.XamlRoot,
                        PrimaryButtonText = "Upload Anyway",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Close,
                        Background = DialogHelper.GetAcrylicBrush(),
                        RequestedTheme = ThemeHelper.GetCurrentTheme()
                    };
                    confirm.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
                    var confirmResult = await confirm.ShowAsync();
                    MemoryOptimizer.ReduceMemory();
                    if (confirmResult != ContentDialogResult.Primary) return;
                }

                await SkinManager.AddOrUpdateProfile(username, data, kind);
                string? localPath = SkinManager.SaveLocalSkin(username, data);
                string msg = $"Profile '{username}' published as {(kind == "public" ? "public" : "private")}";
                msg += localPath != null ? $" and cached locally: {localPath}" : " (local cache write failed)";
                ShowInfo(msg);
                MainWindow.Instance?.RefreshAccounts();
            }
            catch (Exception ex)
            {
                ShowInfo($"Upload failed: {ex.Message}");
            }
            finally
            {
                // always re-render from the local snapshot - a post-success
                // hiccup must never leave a stale list on screen
                SetBusy(null);
                Logger.Info($"Upload finished for '{username}', re-rendering");
                await LoadProfilesAsync();
            }
        }

        private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string username) return;

            var dialog = new ContentDialog
            {
                Title = "Delete profile",
                Content = $"Delete skin profile '{username}'? This removes it from the index.",
                XamlRoot = this.XamlRoot,
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                Background = DialogHelper.GetAcrylicBrush(),
                RequestedTheme = ThemeHelper.GetCurrentTheme()
            };

            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();
            if (result != ContentDialogResult.Primary) return;

            try
            {
                SetBusy($"Deleting '{username}'...");
                try
                {
                    await SkinManager.RemoveProfile(username);
                    SkinManager.DeleteLocalSkin(username);
                    ShowInfo($"Profile '{username}' deleted.");
                    MainWindow.Instance?.RefreshAccounts();
                }
                finally
                {
                    // always re-render from the local snapshot - a post-success
                    // hiccup must never leave a stale list on screen
                    SetBusy(null);
                    Logger.Info($"Delete finished for '{username}', re-rendering");
                    await LoadProfilesAsync();
                }
            }
            catch (Exception ex)
            {
                ShowInfo($"Delete failed: {ex.Message}");
            }
        }

        private async void RenameProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string oldUsername) return;

            var newNameBox = new TextBox
            {
                Header = "New username",
                PlaceholderText = "New in-game name",
                Text = oldUsername
            };

            var dialog = new ContentDialog
            {
                Title = $"Rename '{oldUsername}'",
                Content = newNameBox,
                XamlRoot = this.XamlRoot,
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                Background = DialogHelper.GetAcrylicBrush(),
                RequestedTheme = ThemeHelper.GetCurrentTheme()
            };

            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();
            if (result != ContentDialogResult.Primary) return;

            string newUsername = newNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newUsername) || string.Equals(oldUsername, newUsername, StringComparison.Ordinal))
                return;

            try
            {
                SetBusy($"Renaming '{oldUsername}' → '{newUsername}'...");
                try
                {
                    await SkinManager.RenameProfileAsync(oldUsername, newUsername);
                    ShowInfo($"Renamed '{oldUsername}' → '{newUsername}' (server-verified).");
                    MainWindow.Instance?.RefreshAccounts();
                }
                finally
                {
                    SetBusy(null);
                    Logger.Info($"Rename finished for '{oldUsername}' → '{newUsername}', re-rendering");
                    await LoadProfilesAsync();
                }
            }
            catch (Exception ex)
            {
                ShowInfo($"Rename failed: {ex.Message}");
            }
        }

        private void SetBusy(string? message)
        {
            addProfileButton.IsEnabled = message == null;
        }

        private void ShowInfo(string message)
        {
            infoBar.Message = message;
            infoBar.Visibility = Visibility.Visible;
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadProfilesAsync();
        }
    }

    public partial class ProfileListItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Username { get; set; } = "";
        public string CustomUUID { get; set; } = "";
        public string SkinUrl { get; set; } = "";
        public string Kind { get; set; } = "private";
        public string KindLabel { get; set; } = "Private";
        public string ClaimLabel { get; set; } = "";

        private string _syncStatus = "";
        public string SyncStatus
        {
            get => _syncStatus;
            set { if (_syncStatus == value) return; _syncStatus = value; OnPropertyChanged(nameof(SyncStatus)); }
        }

        private Microsoft.UI.Xaml.Media.Brush _syncBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x8A, 0x8A, 0x8A));
        public Microsoft.UI.Xaml.Media.Brush SyncBrush
        {
            get => _syncBrush;
            set { if (ReferenceEquals(_syncBrush, value)) return; _syncBrush = value; OnPropertyChanged(nameof(SyncBrush)); }
        }

        private ImageSource? _previewImage;
        public ImageSource? PreviewImage
        {
            get => _previewImage;
            set { if (ReferenceEquals(_previewImage, value)) return; _previewImage = value; OnPropertyChanged(nameof(PreviewImage)); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}