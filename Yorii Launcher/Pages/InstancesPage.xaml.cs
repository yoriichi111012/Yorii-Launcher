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

        // setup filesystem watcher so the instance list updates when folders are added/deleted externally
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

        // debounce filesystem changes with a short delay so rapid changes dont hammer the ui
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

            // copy the selected player's local skin into the new instance so the
            // account-box head and in-game skin load instantly, then refresh the
            // account box (it now resolves local skins from the new active path)
            if (AccountManager.GetSelectedAccount() is { } selectedAccount)
            {
                SkinManager.CopyLocalSkinToPath(selectedAccount.Username, instance.MinecraftPath);
                // also pull the latest published skin so a brand-new instance
                // never shows a missing/stale head in the ui or in-game
                _ = SkinManager.SyncSkinToAllInstancesAsync(selectedAccount.Username);
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
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
            };

            var iconButton = new Button
            {
                Content = "Change Icon",
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
                    editPendingIconPath = file.Path;
                    iconText.Text = Path.GetFileName(file.Path);
                }
            };

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(nameBox);
            panel.Children.Add(iconButton);
            panel.Children.Add(iconText);

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