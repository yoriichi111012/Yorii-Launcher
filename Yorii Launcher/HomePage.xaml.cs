using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Yorii_Launcher
{
    public sealed partial class HomePage : Page
    {
        private readonly ObservableCollection<ServerItem> servers = [];
        private readonly ObservableCollection<WorldItem> worlds = [];
        private MinecraftReleaseNotesService? releaseNotesService;
        private bool isLoadingReleaseNotes;
        private bool isUpdatingAutoConnectSelection;
        private MinecraftReleaseNote? lastSelectedReleaseNote;

        public static Visibility IconVisibility(ImageSource? icon) =>
            icon != null ? Visibility.Collapsed : Visibility.Visible;

        public HomePage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
            serversListView.ItemsSource = servers;
            worldsListView.ItemsSource = worlds;

            releaseNotesService = new MinecraftReleaseNotesService(HttpService.Client);
            MainWindow.Instance?.RegisterHomeControls(accountComboBox, versionComboBox, playButton);
            RefreshHomeState();
            MemoryOptimizer.ReduceMemory();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            MainWindow.Instance?.RegisterHomeControls(accountComboBox, versionComboBox, playButton);
            RefreshHomeState();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            servers.Clear();
            worlds.Clear();
        }

        private void RefreshHomeState()
        {
            LoadAutoConnectTargets();
            UpdateSummaryCards();
            LoadReleaseNotesSetting();
        }

        private void LoadAutoConnectTargets()
        {
            isUpdatingAutoConnectSelection = true;
            try
            {
                LoadServers();
                LoadWorlds();
            }
            finally
            {
                isUpdatingAutoConnectSelection = false;
            }

            UpdateAutoConnectSummary();
        }

        private void LoadServers()
        {
            var serverListEnabled = SettingsManager.Current.ServerListEnabled;
            var worldListEnabled = SettingsManager.Current.WorldListEnabled;
            var autoConnectEnabled = serverListEnabled || worldListEnabled;

            serverColumn.Width = autoConnectEnabled ? new GridLength(360) : new GridLength(0);
            autoConnectColumn.Visibility = autoConnectEnabled ? Visibility.Visible : Visibility.Collapsed;
            serverListPanel.Visibility = serverListEnabled ? Visibility.Visible : Visibility.Collapsed;
            serversDisabledPanel.Visibility = serverListEnabled ? Visibility.Collapsed : Visibility.Visible;
            serversListView.Visibility = serverListEnabled ? Visibility.Visible : Visibility.Collapsed;

            if (!serverListEnabled)
            {
                servers.Clear();
                emptyServersText.Visibility = Visibility.Collapsed;
                ServerManager.SetSelectedServerAddress(null);
                return;
            }

            servers.Clear();
            var minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();
            var loadedServers = ServerManager.LoadServersFromMinecraftPath(minecraftPath);
            var selectedAddress = ServerManager.GetSelectedServerAddress();
            ServerItem? selectedItem = null;

            foreach (var server in loadedServers)
            {
                servers.Add(server);
                if (server.Address == selectedAddress)
                    selectedItem = server;
            }

            emptyServersText.Visibility = servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            serversListView.Visibility = servers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            serversListView.SelectedItem = selectedItem;
        }

        private void LoadWorlds()
        {
            var worldListEnabled = SettingsManager.Current.WorldListEnabled;
            worldListPanel.Visibility = worldListEnabled ? Visibility.Visible : Visibility.Collapsed;

            if (!worldListEnabled)
            {
                worlds.Clear();
                emptyWorldsText.Visibility = Visibility.Collapsed;
                WorldManager.SetSelectedWorldId(null);
                return;
            }

            worlds.Clear();
            var minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();
            var loadedWorlds = WorldManager.LoadWorldsFromMinecraftPath(minecraftPath);
            var selectedWorldId = WorldManager.GetSelectedWorldId();
            WorldItem? selectedItem = null;

            foreach (var world in loadedWorlds)
            {
                worlds.Add(world);
                if (world.Id == selectedWorldId)
                    selectedItem = world;
            }

            emptyWorldsText.Visibility = worlds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            worldsListView.Visibility = worlds.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            worldsListView.SelectedItem = selectedItem;
        }

        private void UpdateSummaryCards()
        {
            // the instance card doubles as the instances entry point: it shows
            // the active instance (or default-folder text when disabled) and
            // navigates to the instances page only while instances are enabled
            if (SettingsManager.Current.InstancesEnabled)
            {
                var selectedInstance = InstanceManager.GetSelectedInstance();
                instanceSummaryText.Text = selectedInstance?.Name ?? "No instance selected";
            }
            else
            {
                instanceSummaryText.Text = "Default Minecraft folder";
            }

            memorySummaryText.Text = $"{SettingsManager.Current.RamAmount:0.#} GB allocated";

            UpdateAutoConnectSummary();
        }

        private void UpdateAutoConnectSummary()
        {
            if (!SettingsManager.Current.ServerListEnabled && !SettingsManager.Current.WorldListEnabled)
            {
                serverSummaryText.Text = "Auto-connect lists are off";
                clearAutoConnectButton.Visibility = Visibility.Collapsed;
                return;
            }

            if (SettingsManager.Current.WorldListEnabled && worldsListView.SelectedItem is WorldItem world)
            {
                serverSummaryText.Text = $"World: {world.Name}";
                clearAutoConnectButton.Visibility = Visibility.Visible;
                return;
            }

            if (SettingsManager.Current.ServerListEnabled && serversListView.SelectedItem is ServerItem server)
            {
                serverSummaryText.Text = $"Server: {server.Name}";
                clearAutoConnectButton.Visibility = Visibility.Visible;
                return;
            }

            serverSummaryText.Text = "No auto-connect target";
            clearAutoConnectButton.Visibility = Visibility.Collapsed;
        }

        private void ServersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingAutoConnectSelection)
                return;

            if (serversListView.SelectedItem is ServerItem server)
            {
                ServerManager.SetSelectedServerAddress(server.Address);
                WorldManager.SetSelectedWorldId(null);
                worldsListView.SelectedItem = null;
            }
            else
            {
                ServerManager.SetSelectedServerAddress(null);
            }

            UpdateAutoConnectSummary();
        }

        private void WorldsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingAutoConnectSelection)
                return;

            if (worldsListView.SelectedItem is WorldItem world)
            {
                WorldManager.SetSelectedWorldId(world.Id);
                ServerManager.SetSelectedServerAddress(null);
                serversListView.SelectedItem = null;
            }
            else
            {
                WorldManager.SetSelectedWorldId(null);
            }

            UpdateAutoConnectSummary();
        }

        private void ClearAutoConnectButton_Click(object sender, RoutedEventArgs e)
        {
            isUpdatingAutoConnectSelection = true;
            try
            {
                ServerManager.SetSelectedServerAddress(null);
                WorldManager.SetSelectedWorldId(null);
                serversListView.SelectedItem = null;
                worldsListView.SelectedItem = null;
            }
            finally
            {
                isUpdatingAutoConnectSelection = false;
            }

            UpdateAutoConnectSummary();
        }

        private void LoadReleaseNotesSetting()
        {
            var showReleaseNotes = SettingsManager.Current.ShowReleaseNotesOnHome;
            releaseNotesPanel.Visibility = showReleaseNotes ? Visibility.Visible : Visibility.Collapsed;

            if (showReleaseNotes && releaseNotesVersionComboBox.ItemsSource == null)
                _ = LoadReleaseNotesAsync();
        }

        private async Task LoadReleaseNotesAsync()
        {
            if (isLoadingReleaseNotes)
                return;

            isLoadingReleaseNotes = true;
            releaseNotesProgressRing.IsActive = true;
            releaseNotesProgressRing.Visibility = Visibility.Visible;
            releaseNotesSummaryText.Text = "Loading Minecraft release notes...";

            try
            {
                releaseNotesService ??= new MinecraftReleaseNotesService(HttpService.Client);

                var entries = await releaseNotesService.GetReleaseNotesAsync();
                releaseNotesVersionComboBox.ItemsSource = entries;

                var match = lastSelectedReleaseNote == null
                    ? null
                    : entries.FirstOrDefault(e =>
                        string.Equals(e.Version, lastSelectedReleaseNote.Version, StringComparison.OrdinalIgnoreCase));

                // prefer the newest version that actually has notes so the
                // heading doesn't open on a placeholder for an unreleased
                // snapshot mojang hasn't published notes for yet
                match ??= entries.FirstOrDefault(e => e.HasChangelog) ?? entries.FirstOrDefault();

                if (match != null)
                {
                    lastSelectedReleaseNote = match;
                    releaseNotesVersionComboBox.SelectedItem = match;
                    await ApplyReleaseNotePreviewAsync(match);
                }
            }
            catch
            {
                releaseNotesTitleText.Text = "Release notes unavailable";
                releaseNotesVersionText.Text = "";
                releaseNotesSummaryText.Text = "Could not load Minecraft release notes right now.";
            }
            finally
            {
                releaseNotesProgressRing.IsActive = false;
                releaseNotesProgressRing.Visibility = Visibility.Collapsed;
                isLoadingReleaseNotes = false;
            }
        }

        private async void ReleaseNotesVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingReleaseNotes)
                return;

            if (releaseNotesVersionComboBox.SelectedItem is MinecraftReleaseNote note)
            {
                lastSelectedReleaseNote = note;
                await ApplyReleaseNotePreviewAsync(note);
            }
        }

        private async Task ApplyReleaseNotePreviewAsync(MinecraftReleaseNote note)
        {
            releaseNotesTitleText.Text = string.IsNullOrWhiteSpace(note.Title)
                ? $"Minecraft {note.Version}"
                : note.Title;

            releaseNotesVersionText.Text = note.Date == default
                ? note.Version
                : $"{note.Version} - {note.Date:MMM d, yyyy}";

            var intro = await GetReleaseNoteIntroAsync(note);

            if (lastSelectedReleaseNote == note)
                releaseNotesSummaryText.Text = intro;
        }

        private async Task<string> GetReleaseNoteIntroAsync(MinecraftReleaseNote note)
        {
            if (note.HasChangelog)
            {
                releaseNotesService ??= new MinecraftReleaseNotesService(HttpService.Client);

                try
                {
                    var html = await releaseNotesService.GetReleaseNoteHtmlAsync(note);
                    var firstParagraph = ExtractFirstParagraphText(html);

                    if (!string.IsNullOrWhiteSpace(firstParagraph))
                        return firstParagraph;
                }
                catch
                {
                    // fall back to the feed summary if the full body cannot be loaded
                }
            }

            return string.IsNullOrWhiteSpace(note.ShortText)
                ? "Open the full release notes to see the changelog."
                : note.ShortText.Trim();
        }

        private static string ExtractFirstParagraphText(string html)
        {
            var match = FirstParagraphRegex().Match(html);

            if (!match.Success)
                return "";

            var paragraph = BreakRegex().Replace(match.Groups[1].Value, " ");
            paragraph = TagRegex().Replace(paragraph, "");
            paragraph = WebUtility.HtmlDecode(paragraph);

            return WhitespaceRegex().Replace(paragraph, " ").Trim();
        }

        [GeneratedRegex(@"<p\b[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex FirstParagraphRegex();

        [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
        private static partial Regex BreakRegex();

        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex TagRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();

        private void OpenReleaseNotesButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.NavigateToReleaseNotes(lastSelectedReleaseNote?.Version);
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.PlayButton_Click(sender, e);
        }

        private void AccountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MainWindow.Instance?.AccountComboBox_SelectionChanged(sender, e);
        }

        private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MainWindow.Instance?.VersionList_SelectionChanged(sender, e);
        }

        private void VersionList_DropDownClosed(object sender, object e)
        {
            MainWindow.Instance?.VersionList_DropDownClosed(sender, e);
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var path = SettingsManager.Current.GetActiveMinecraftPath();

            if (!string.IsNullOrWhiteSpace(path))
                Process.Start("explorer.exe", path);
        }

        private void InstancesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!SettingsManager.Current.InstancesEnabled)
                return;

            MainWindow.Instance?.SelectSection("instances");
        }

        // the instance card is a plain border with tapped (not a button) so its
        // appearance is pixel-identical at rest and on hover — no control chrome
        private void InstanceCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            InstancesButton_Click(sender, e);
        }

        private void ModsButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.SelectSection("extensions");
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.SelectSection("settings");
        }

        private void EditMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.NavigateToSettings("memory");
        }

        private async void AddServerButton_Click(object sender, RoutedEventArgs e)
        {
            var server = await ShowServerDialog("Add Server", null);

            if (server == null)
                return;

            ServerManager.AddServer(SettingsManager.Current.GetActiveMinecraftPath(), server);
            LoadAutoConnectTargets();
        }

        private async void EditServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ServerItem server)
                return;

            var updatedServer = await ShowServerDialog("Edit Server", server);

            if (updatedServer == null)
                return;

            ServerManager.UpdateServer(SettingsManager.Current.GetActiveMinecraftPath(), server.Address, updatedServer);
            LoadAutoConnectTargets();
        }

        private async void DeleteServer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ServerItem server)
                return;

            var dialog = new ContentDialog
            {
                Title = "Delete server?",
                Content = $"This will remove \"{server.Name}\" from servers.dat.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                Background = DialogHelper.GetAcrylicBrush(),
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeHelper.GetCurrentTheme()
            };

            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();

            if (result != ContentDialogResult.Primary)
                return;

            ServerManager.DeleteServer(SettingsManager.Current.GetActiveMinecraftPath(), server.Address);
            LoadAutoConnectTargets();
        }

        private async void AddWorldButton_Click(object sender, RoutedEventArgs e)
        {
            var worldName = await ShowWorldDialog("Create World", null);

            if (string.IsNullOrWhiteSpace(worldName))
                return;

            try
            {
                var world = WorldManager.CreateWorld(SettingsManager.Current.GetActiveMinecraftPath(), worldName);

                if (world == null)
                    return;

                WorldManager.SetSelectedWorldId(world.Id);
                ServerManager.SetSelectedServerAddress(null);
                LoadAutoConnectTargets();
            }
            catch (Exception ex)
            {
                Logger.Error($"Create world failed: {ex.Message}");
                NotificationHelper.Show("World not created", ex.Message);
            }
        }

        private async void EditWorld_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not WorldItem world)
                return;

            var worldName = await ShowWorldDialog("Edit World", world);

            if (string.IsNullOrWhiteSpace(worldName))
                return;

            try
            {
                var updatedWorld = WorldManager.RenameWorld(SettingsManager.Current.GetActiveMinecraftPath(), world, worldName);

                if (updatedWorld == null)
                    return;

                LoadAutoConnectTargets();
            }
            catch (Exception ex)
            {
                Logger.Error($"Edit world failed: {ex.Message}");
                NotificationHelper.Show("World not edited", ex.Message);
            }
        }

        private async void DeleteWorld_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not WorldItem world)
                return;

            var dialog = new ContentDialog
            {
                Title = "Delete world?",
                Content = $"This will delete \"{world.Name}\" from the saves folder.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                Background = DialogHelper.GetAcrylicBrush(),
                RequestedTheme = ThemeHelper.GetCurrentTheme()
            };

            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();

            if (result != ContentDialogResult.Primary)
                return;

            try
            {
                WorldManager.DeleteWorld(SettingsManager.Current.GetActiveMinecraftPath(), world);
                LoadAutoConnectTargets();
            }
            catch (Exception ex)
            {
                Logger.Error($"Delete world failed: {ex.Message}");
                NotificationHelper.Show("World not deleted", ex.Message);
            }
        }

        private async Task<ServerItem?> ShowServerDialog(string title, ServerItem? server)
        {
            var nameBox = new TextBox
            {
                Header = "Name",
                PlaceholderText = "Minecraft Server",
                Text = server?.Name ?? ""
            };

            var addressBox = new TextBox
            {
                Header = "Address",
                PlaceholderText = "example.org or example.org:25565",
                Text = server?.Address ?? ""
            };

            var panel = new StackPanel
            {
                Spacing = 10
            };

            panel.Children.Add(nameBox);
            panel.Children.Add(addressBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                Background = DialogHelper.GetAcrylicBrush(),
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeHelper.GetCurrentTheme()
            };

            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();

            if (result != ContentDialogResult.Primary)
                return null;

            var address = addressBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(address))
                return null;

            var name = nameBox.Text.Trim();

            return new ServerItem
            {
                Id = address,
                Name = string.IsNullOrWhiteSpace(name) ? address : name,
                Address = address,
                IconData = server?.IconData ?? ""
            };
        }

        private async Task<string?> ShowWorldDialog(string title, WorldItem? world)
        {
            var nameBox = new TextBox
            {
                Header = "Name",
                PlaceholderText = "World name",
                Text = world?.FolderName ?? ""
            };

            var dialog = new ContentDialog
            {
                Title = title,
                Content = nameBox,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                Background = DialogHelper.GetAcrylicBrush(),
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeHelper.GetCurrentTheme()
            };

            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();

            if (result != ContentDialogResult.Primary)
                return null;

            var name = nameBox.Text.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }
}