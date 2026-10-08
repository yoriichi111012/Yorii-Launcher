using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Yorii_Launcher.Pages
{
    public sealed partial class InstancesPage : Page
    {
        private readonly ObservableCollection<LauncherInstance> instances = [];
        private string? pendingIconPath;
        private FileSystemWatcher? instancesWatcher;
        private CancellationTokenSource? watcherCts;

        public InstancesPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            instancesGrid.ItemsSource = instances;

            LoadInstances();
            StartInstancesWatcher();
            MemoryOptimizer.ReduceMemory();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            instances.Clear();
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Logger.Info("Navigated to InstancesPage");
            LoadInstances();
        }

        // toggle right column based on settings
        // toggle right column based on settings
        private void LoadInstances()
        {
            var instancesEnabled = SettingsManager.Current.InstancesEnabled;

            instancesDisabledPanel.Visibility = instancesEnabled ? Visibility.Collapsed : Visibility.Visible;
            emptyInstancesText.Visibility = Visibility.Collapsed;
            instancesGrid.Visibility = instancesEnabled ? Visibility.Visible : Visibility.Collapsed;
            createInstanceButton.Visibility = instancesEnabled ? Visibility.Visible : Visibility.Collapsed;

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

            // yoriiskinsloader is a fork of customskinloader optimized for faster skin loading and other improvements
            InstanceManager.EnsureYoriiSkinsLoaderInstalled();
        }

        // setup filesystem watcher so the instance list updates when folders are added/deleted externally.
        // same locked swap as InstalledModsPage: watcher events arrive on pool
        // threads while Start runs on the ui thread
        private readonly object watcherLock = new();
        private void StartInstancesWatcher()
        {
            Directory.CreateDirectory(InstanceManager.InstancesRoot);

            FileSystemWatcher? oldWatcher;
            CancellationTokenSource? oldCts;
            lock (watcherLock)
            {
                oldWatcher = instancesWatcher;
                oldCts = watcherCts;
                instancesWatcher = new FileSystemWatcher(InstanceManager.InstancesRoot)
                {
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                instancesWatcher.Created += InstancesChanged;
                instancesWatcher.Deleted += InstancesChanged;
                instancesWatcher.Renamed += InstancesChanged;
                watcherCts = null;
            }
            oldWatcher?.Dispose();
            CancelAndDisposeCts(oldCts);
            Logger.Info($"Instances watcher watching {InstanceManager.InstancesRoot}");
        }

        private static void CancelAndDisposeCts(CancellationTokenSource? cts)
        {
            if (cts is null) return;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        // debounce filesystem changes with a short delay so rapid changes dont hammer the ui
        private void InstancesChanged(object sender, FileSystemEventArgs e)
        {
            CancellationTokenSource? oldCts;
            CancellationTokenSource freshCts;
            lock (watcherLock)
            {
                oldCts = watcherCts;
                freshCts = new CancellationTokenSource();
                watcherCts = freshCts;
            }
            CancelAndDisposeCts(oldCts);
            var token = freshCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, token);

                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        LoadInstances();
                        // loadservers();
                        await (MainWindow.Instance?.RefreshInstanceContextAsync() ?? Task.CompletedTask);
                    });
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        // show create dialog, make instance, refresh ui. had to build this whole dialog in code cause xaml was fighting me
        private static (ToggleSwitch toggle, ComboBox box) AddCopyRow(StackPanel panel, string label, System.Collections.Generic.List<LauncherInstance> sources)
        {
            var labelText = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center
            };

            var toggle = new ToggleSwitch
            {
                OffContent = "",
                OnContent = "",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, -8, 0) // optional: trims the template's empty content gap
            };
            toggle.Resources["ToggleSwitchThemeMinWidth"] = 0.0; // default is 154, which pushes things apart

            var box = new ComboBox
            {
                IsEnabled = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (Application.Current.Resources.TryGetValue("AcrylicComboBoxStyle", out object resource) && resource is Style acrylicStyle)
                box.Style = acrylicStyle;

            foreach (var source in sources)
                box.Items.Add(new ComboBoxItem { Content = source.Name, Tag = source });

            var selected = InstanceManager.GetSelectedInstance();
            box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as LauncherInstance)?.Id == selected?.Id)
                ?? box.Items.OfType<ComboBoxItem>().FirstOrDefault();

            toggle.Toggled += (_, _) => box.IsEnabled = toggle.IsOn;

            var row = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ColumnSpacing = 12,
                ColumnDefinitions =
        {
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            new ColumnDefinition { Width = GridLength.Auto },
            new ColumnDefinition { Width = GridLength.Auto }
        }
            };

            Grid.SetColumn(labelText, 0);
            Grid.SetColumn(toggle, 1);
            Grid.SetColumn(box, 2);

            row.Children.Add(labelText);
            row.Children.Add(toggle);
            row.Children.Add(box);

            panel.Children.Add(row);
            return (toggle, box);
        }

        private async Task CopyFromSourceAsync(LauncherInstance instance, string what, ToggleSwitch? toggle, ComboBox? box)
        {
            if (toggle?.IsOn != true || box?.SelectedItem is not ComboBoxItem item || item.Tag is not LauncherInstance source)
                return;

            try
            {
                if (what == "worlds")
                {
                    var from = Path.Combine(source.MinecraftPath, "saves");
                    if (!Directory.Exists(from))
                        return;
                    int worlds = Directory.EnumerateDirectories(from).Count();
                    await Task.Run(() => CopyDirectory(from, Path.Combine(instance.MinecraftPath, "saves")));
                    NotificationHelper.Show("Instance created", $"Copied {worlds} worlds from '{source.Name}'.");
                }
                else
                {
                    var file = what == "servers" ? "servers.dat" : "options.txt";
                    var from = Path.Combine(source.MinecraftPath, file);
                    if (!File.Exists(from))
                        return;
                    File.Copy(from, Path.Combine(instance.MinecraftPath, file), true);
                    NotificationHelper.Show("Instance created", $"Copied {what} from '{source.Name}'.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to copy {what} from '{source.Name}': {ex.Message}");
                NotificationHelper.Show("Copy failed", $"Could not copy {what} from '{source.Name}'.");
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (var dir in Directory.EnumerateDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        private async void CreateInstance_Click(object sender, RoutedEventArgs e)
        {
            pendingIconPath = null;
            var nameBox = new TextBox
            {
                Header = "Name",
                PlaceholderText = "New Instance"
            };

            var iconText = new TextBlock
            {
                Text = "No icon selected",
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
            };

            var iconButton = new Button
            {
                Content = "Choose Icon",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
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

            var icon = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ColumnSpacing = 10,
                ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
            };

            Grid.SetColumn(iconText, 0);
            Grid.SetColumn(iconButton, 1);

            icon.Children.Add(iconText);
            icon.Children.Add(iconButton);

            panel.Children.Add(icon);

            var copySources = InstanceManager.LoadInstances();
            ToggleSwitch? settingsToggle = null;
            ComboBox? settingsBox = null;
            ToggleSwitch? worldsToggle = null;
            ComboBox? worldsBox = null;
            ToggleSwitch? serversToggle = null;
            ComboBox? serversBox = null;

            if (copySources.Count > 0)
            {

                panel.Children.Add(new TextBlock
                {
                    Text = "Copy from another instance",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });

                (settingsToggle, settingsBox) = AddCopyRow(panel, "Settings", copySources);
                (worldsToggle, worldsBox) = AddCopyRow(panel, "Worlds", copySources);
                (serversToggle, serversBox) = AddCopyRow(panel, "Servers", copySources);
            }

            ElementTheme theme = ThemeHelper.GetCurrentTheme();

            var dialog = new ContentDialog
            {
                Title = "Create instance",
                Content = panel,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                Background = DialogHelper.GetAcrylicBrush(),
                XamlRoot = XamlRoot,
                RequestedTheme = theme,
            };

            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();

            if (result != ContentDialogResult.Primary)
                return;

            var name = nameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
                name = "New instance";

            var scale = XamlRoot?.RasterizationScale ?? 1.0;
            var instance = InstanceManager.CreateInstance(name, pendingIconPath, scale);

            await CopyFromSourceAsync(instance, "settings", settingsToggle, settingsBox);
            await CopyFromSourceAsync(instance, "worlds", worldsToggle, worldsBox);
            await CopyFromSourceAsync(instance, "servers", serversToggle, serversBox);

            // copy the selected player's local skin into the new instance so the
            // account-box head and in-game skin load instantly, then refresh the
            // account box (it now resolves local skins from the new active path)
            if (AccountManager.GetSelectedAccount() is { } selectedAccount)
            {
                SkinManager.CopyLocalSkinToPath(selectedAccount.Username, instance.MinecraftPath);
                SkinManager.CopyLocalCapeToPath(selectedAccount.Username, instance.MinecraftPath);
                // offline accounts have no published skin: the local copy above
                // is the truth, a remote sync would clobber it with any
                // same-name published skin
                if (!selectedAccount.IsOffline)
                {
                    // also pull the latest published skin so a brand-new instance
                    // never shows a missing/stale head in the ui or in-game
                    _ = SkinManager.SyncSkinToAllInstancesAsync(selectedAccount.Username);
                    _ = SkinManager.SyncCapeToAllInstancesAsync(selectedAccount.Username);
                }
            }

            InstanceManager.SetSelectedInstance(instance.Id);

            LoadInstances();
            // loadservers();
            await (MainWindow.Instance?.RefreshInstanceContextAsync() ?? Task.CompletedTask);
            MainWindow.Instance?.RefreshAccounts();
        }

        private async void InstancesGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not LauncherInstance instance)
                return;

            InstanceManager.SetSelectedInstance(instance.Id);
            instancesGrid.SelectedItem = instance;

            // loadservers();

            await (MainWindow.Instance?.RefreshInstanceContextAsync() ?? Task.CompletedTask);
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
                Background = DialogHelper.GetAcrylicBrush(),
                XamlRoot = XamlRoot,
                RequestedTheme = theme
            };

            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();

            if (result != ContentDialogResult.Primary)
                return;

            InstanceManager.DeleteInstance(instance);
            LoadInstances();
            // loadservers();
            await (MainWindow.Instance?.RefreshInstanceContextAsync() ?? Task.CompletedTask);
        }

        private string? editPendingIconPath;

        private async void EditInstanceClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not LauncherInstance instance)
                return;

            editPendingIconPath = null;

            var nameBox = new TextBox
            {
                Header = "Name",
                Text = instance.Name,
                PlaceholderText = "Instance name",
                SelectionStart = instance.Name.Length
            };

            var iconText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(instance.IconPath) ? "No icon" : Path.GetFileName(instance.IconPath),
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
            };

            var iconButton = new Button
            {
                Content = "Change Icon",
                VerticalAlignment = VerticalAlignment.Center
            };

            var iconRow = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ColumnSpacing = 10,
                ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
            };

            Grid.SetColumn(iconText, 0);
            Grid.SetColumn(iconButton, 1);
            iconRow.Children.Add(iconText);
            iconRow.Children.Add(iconButton);

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(nameBox);
            panel.Children.Add(iconRow);

            ElementTheme theme = ThemeHelper.GetCurrentTheme();

            var dialog = new ContentDialog
            {
                Title = "Edit instance",
                Content = panel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                Background = DialogHelper.GetAcrylicBrush(),
                XamlRoot = XamlRoot,
                RequestedTheme = theme
            };

            dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
            var result = await dialog.ShowAsync();
            MemoryOptimizer.ReduceMemory();

            if (result != ContentDialogResult.Primary)
                return;

            var name = nameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
                return;

            InstanceManager.RenameInstance(instance, name);

            // change icon if user picked one
            if (!string.IsNullOrWhiteSpace(editPendingIconPath))
            {
                var extension = Path.GetExtension(editPendingIconPath);
                if (string.IsNullOrWhiteSpace(extension))
                    extension = ".png";

                var iconFileName = "icon" + extension;
                var destPath = Path.Combine(instance.InstancePath, iconFileName);
                File.Copy(editPendingIconPath, destPath, true);

                // update metadata with new icon path
                var instancePath = Path.Combine(instance.InstancePath, "instance.yaml");
                if (File.Exists(instancePath))
                {
                    var yaml = File.ReadAllText(instancePath);
                    var metadata = Helpers.LauncherYaml.Deserialize<InstanceMetadata>(yaml);
                    if (metadata != null)
                    {
                        metadata.IconPath = iconFileName;
                        File.WriteAllText(instancePath, Helpers.LauncherYaml.Serialize(metadata));
                    }
                }
            }

            LoadInstances();
            await (MainWindow.Instance?.RefreshInstanceContextAsync() ?? Task.CompletedTask);
        }
    }
}