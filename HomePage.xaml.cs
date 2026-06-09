using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;

namespace Yorii_Launcher
{
    public sealed partial class HomePage : Page
    {
        public static Visibility IconVisibility(ImageSource? icon) =>
            icon != null ? Visibility.Collapsed : Visibility.Visible;

        private readonly ObservableCollection<LauncherInstance> instances = [];
        private readonly ObservableCollection<ServerItem> servers = [];
        private string? pendingIconPath;
        private FileSystemWatcher? instancesWatcher;
        private CancellationTokenSource? watcherCts;
        private MinecraftReleaseNotesService? releaseNotesService;
        private bool isLoadingReleaseNotes;
        private CancellationTokenSource? loadCts;
        private MinecraftReleaseNote? lastSelectedReleaseNote;

        public HomePage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
            instancesGrid.ItemsSource = instances;
            serversListView.ItemsSource = servers;
            releaseNotesService = new MinecraftReleaseNotesService(HttpService.Client);
            LoadInstances();
            LoadServers();
            StartInstancesWatcher();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadInstances();
            LoadServers();

            // reset progress state before reloading
            releaseNotesProgressRing.IsActive = false;
            releaseNotesProgressRing.Visibility = Visibility.Collapsed;
            releaseNotesErrorText.Visibility = Visibility.Collapsed;
            isLoadingReleaseNotes = false;

            _ = LoadReleaseNotesAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            instances.Clear();
            servers.Clear();
        }

        // toggle right column based on settings
        private void LoadInstances()
        {
            var instancesEnabled = SettingsManager.Current.InstancesEnabled;
            var serverEnabled = SettingsManager.Current.ServerListEnabled;
            instancesPanel.Visibility = instancesEnabled ? Visibility.Visible : Visibility.Collapsed;

            var showRightColumn = instancesEnabled || serverEnabled;

            if (showRightColumn)
            {
                contentGrid.ColumnDefinitions[1].Width = new GridLength(420);

                if (instancesEnabled)
                {
                    instancesPanel.SetValue(Grid.RowProperty, 0);
                    serverListPanel.SetValue(Grid.RowProperty, 1);
                    instancesRow.Height = new GridLength(1, GridUnitType.Star);
                    serverRow.Height = serverEnabled ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                }
                else
                {
                    instancesRow.Height = new GridLength(0);
                    serverRow.Height = new GridLength(1, GridUnitType.Star);
                }
            }
            else
            {
                contentGrid.ColumnDefinitions[1].Width = new GridLength(0);
                instancesRow.Height = new GridLength(0);
                serverRow.Height = new GridLength(0);
            }

            contentGrid.ColumnSpacing = showRightColumn ? 20 : 0;

            if (!instancesEnabled)
            {
                instances.Clear();
                return;
            }

            var selectedId = InstanceManager.GetSelectedInstanceId();
            var selectedStillExists = false;

            instances.Clear();

            var scale = XamlRoot?.RasterizationScale ?? 1.0;

            foreach (var instance in InstanceManager.LoadInstances(scale))
            {
                instances.Add(instance);

                if (instance.Id == selectedId)
                    selectedStillExists = true;
            }

            if (!string.IsNullOrWhiteSpace(selectedId) && !selectedStillExists)
            {
                InstanceManager.ClearSelectedInstance();
                selectedId = null;
            }

            instancesGrid.SelectedItem = null;
            foreach (var item in instances)
            {
                if (item.Id == selectedId)
                {
                    instancesGrid.SelectedItem = item;
                    break;
                }
            }

            emptyInstancesText.Visibility = instances.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // setup filesystem watcher on instances folder
        private void StartInstancesWatcher()
        {
            Directory.CreateDirectory(InstanceManager.InstancesRoot);

            instancesWatcher?.Dispose();
            instancesWatcher = new FileSystemWatcher(InstanceManager.InstancesRoot)
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            instancesWatcher.Created += InstancesChanged;
            instancesWatcher.Deleted += InstancesChanged;
            instancesWatcher.Renamed += InstancesChanged;
        }

        // debounce filesystem changes with a short delay
        private void InstancesChanged(object sender, FileSystemEventArgs e)
        {
            watcherCts?.Cancel();
            watcherCts = new CancellationTokenSource();
            var token = watcherCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, token);

                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        LoadInstances();
                        LoadServers();
                        await (MainWindow.Instance?.RefreshInstanceContextAsync() ?? Task.CompletedTask);
                    });
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        // show create dialog, make instance, refresh ui
        private async void CreateInstance_Click(object sender, RoutedEventArgs e)
        {
            pendingIconPath = null;

            var nameBox = new TextBox
            {
                Header = "Name",
                PlaceholderText = "New instance"
            };

            var iconText = new TextBlock
            {
                Text = "No icon selected",
                Opacity = 0.7,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
            };

            var iconButton = new Button
            {
                Content = "Choose Icon",
                HorizontalAlignment = HorizontalAlignment.Left
            };

            iconButton.Click += async (_, __) =>
            {
                var picker = new FileOpenPicker(iconButton.XamlRoot.ContentIslandEnvironment.AppWindowId)
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    ViewMode = PickerViewMode.Thumbnail
                };

                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                var file = await picker.PickSingleFileAsync();

                if (file != null)
                {
                    pendingIconPath = file.Path;
                    iconText.Text = Path.GetFileName(file.Path);
                }
            };

            var panel = new StackPanel
            {
                Spacing = 10
            };

            panel.Children.Add(nameBox);
            panel.Children.Add(iconButton);
            panel.Children.Add(iconText);

            ElementTheme theme = ThemeHelper.GetCurrentTheme();

            var dialog = new ContentDialog
            {
                Title = "Create Instance",
                Content = panel,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = theme,
            };

            var result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
                return;

            var name = nameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
                name = "New instance";

            var scale = XamlRoot?.RasterizationScale ?? 1.0;
            var instance = InstanceManager.CreateInstance(name, pendingIconPath, scale);
            InstanceManager.SetSelectedInstance(instance.Id);

            LoadInstances();
            LoadServers();
            await (MainWindow.Instance?.RefreshInstanceContextAsync() ?? Task.CompletedTask);
        }

        private async void InstancesGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not LauncherInstance instance)
                return;

            InstanceManager.SetSelectedInstance(instance.Id);
            instancesGrid.SelectedItem = instance;

            LoadServers();

            await (MainWindow.Instance?.RefreshInstanceContextAsync() ?? Task.CompletedTask);
        }

        private void OpenInstanceFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not LauncherInstance instance)
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = instance.InstancePath,
                UseShellExecute = true
            });
        }

        private async void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not LauncherInstance instance)
                return;

            ElementTheme theme = ThemeHelper.GetCurrentTheme();

            var dialog = new ContentDialog
            {
                Title = "Delete instance?",
                Content = $"This will delete \"{instance.Name}\" and its Minecraft folder.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = theme
            };

            var result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
                return;

            InstanceManager.DeleteInstance(instance);
            LoadInstances();
            LoadServers();
            await (MainWindow.Instance?.RefreshInstanceContextAsync() ?? Task.CompletedTask);
        }

        // load servers from servers.dat in active minecraft path
        private void LoadServers()
        {
            var serverListEnabled = SettingsManager.Current.ServerListEnabled;
            serverListPanel.Visibility = serverListEnabled ? Visibility.Visible : Visibility.Collapsed;

            if (!serverListEnabled)
            {
                servers.Clear();
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
            serversListView.SelectedItem = selectedItem;
        }

        private void ServersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (serversListView.SelectedItem is ServerItem server)
                ServerManager.SetSelectedServerAddress(server.Address);
            else
                ServerManager.SetSelectedServerAddress(null);
        }

        // fetch release notes, match to selected version
        private async Task LoadReleaseNotesAsync()
        {
            if (isLoadingReleaseNotes)
                return;

            isLoadingReleaseNotes = true;
            releaseNotesErrorText.Visibility = Visibility.Collapsed;

            try
            {
                if (releaseNotesService == null)
                    releaseNotesService = new MinecraftReleaseNotesService(HttpService.Client);

                var entries = await releaseNotesService.GetReleaseNotesAsync();
                releaseNotesVersionComboBox.ItemsSource = entries;

                // preserve user's manual selection, otherwise default to latest
                MinecraftReleaseNote? match = null;

                if (lastSelectedReleaseNote != null)
                    match = entries.FirstOrDefault(e =>
                        string.Equals(e.Version, lastSelectedReleaseNote.Version, StringComparison.OrdinalIgnoreCase));

                match ??= entries.FirstOrDefault();

                if (match != null)
                {
                    releaseNotesVersionComboBox.SelectedItem = match;
                    await LoadReleaseNoteHtmlAsync(match, CancellationToken.None);
                }
            }
            catch
            {
                releaseNotesScrollViewer.Visibility = Visibility.Collapsed;
                releaseNotesErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                releaseNotesProgressRing.IsActive = false;
                releaseNotesProgressRing.Visibility = Visibility.Collapsed;
                isLoadingReleaseNotes = false;
            }
        }

        // cancel previous load if switching versions fast
        private async void ReleaseNotesVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingReleaseNotes)
                return;

            loadCts?.Cancel();
            loadCts = new CancellationTokenSource();

            if (releaseNotesVersionComboBox.SelectedItem is MinecraftReleaseNote note)
            {
                lastSelectedReleaseNote = note;

                try
                {
                    await LoadReleaseNoteHtmlAsync(note, loadCts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        // render html then wait 1s and release memory
        private async Task LoadReleaseNoteHtmlAsync(MinecraftReleaseNote releaseNote, CancellationToken ct)
        {
            releaseNotesErrorText.Visibility = Visibility.Collapsed;

            if (releaseNotesService == null)
                releaseNotesService = new MinecraftReleaseNotesService(HttpService.Client);

            // only show progress ring when fetching from the internet
            var cached = await releaseNotesService.IsHtmlCached(releaseNote);
            if (!cached)
            {
                releaseNotesProgressRing.IsActive = true;
                releaseNotesProgressRing.Visibility = Visibility.Visible;
            }

            try
            {

                var html = await releaseNotesService.GetReleaseNoteHtmlAsync(releaseNote);
                ct.ThrowIfCancellationRequested();

                var rendered = ReleaseNotesRenderer.RenderCached(releaseNote.Version, html);
                html = null;

                releaseNotesContentControl.Content = rendered;
                releaseNotesScrollViewer.Visibility = Visibility.Visible;

                await Task.Delay(1000, ct);
                MemoryOptimizer.ReduceMemory();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                releaseNotesScrollViewer.Visibility = Visibility.Collapsed;
                releaseNotesErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                releaseNotesProgressRing.IsActive = false;
                releaseNotesProgressRing.Visibility = Visibility.Collapsed;
            }
        }
    }
}
