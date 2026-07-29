using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using File = System.IO.File;

namespace Yorii_Launcher.Pages
{
    public sealed partial class InstalledResourcePacksPage : Page
    {
        private readonly ObservableCollection<ManagedFileItem> ResourcePacks = [];
        private List<ManagedFileItem> resourcePacks = [];
        private FileSystemWatcher? resourcePacksWatcher;
        private CancellationTokenSource? watcherCts;
        private bool isLoadingResourcePacks;
        private bool ignoreWatcherChanges;

        public static Visibility IconVisibility(ImageSource? icon) =>
            icon != null ? Visibility.Collapsed : Visibility.Visible;

        public InstalledResourcePacksPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            ResourcePacksList.ItemsSource = ResourcePacks;
            var savedMode = (PluginViewMode)SettingsManager.Current.InstalledResourcePacksViewMode;
            PluginViewModeHelper.Apply(ResourcePacksList, savedMode);
            ResourcePacksViewModeSegmented.SelectedIndex = (int)savedMode;

            _ = LoadResourcePacks();
            StartResourcePacksWatcher();

            MemoryOptimizer.ReduceMemory();
        }

        private void ViewModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PluginViewModeHelper.ApplyFromSelectedIndex(ResourcePacksList, ResourcePacksViewModeSegmented.SelectedIndex);
            SettingsManager.Current.InstalledResourcePacksViewMode = ResourcePacksViewModeSegmented.SelectedIndex;
            SettingsManager.SaveSettings();
        }

        private string ResourcePacksFolder
        {
            get
            {
                var minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();
                return Path.Combine(minecraftPath, "resourcepacks");
            }
        }

        private void StartResourcePacksWatcher()
        {
            Directory.CreateDirectory(ResourcePacksFolder);

            resourcePacksWatcher?.Dispose();
            resourcePacksWatcher = new FileSystemWatcher(ResourcePacksFolder);
            resourcePacksWatcher.Created += ResourcePacksChanged;
            resourcePacksWatcher.Deleted += ResourcePacksChanged;
            resourcePacksWatcher.Renamed += ResourcePacksChanged;
            resourcePacksWatcher.EnableRaisingEvents = true;
        }

        private void ResourcePacksChanged(object sender, FileSystemEventArgs e)
        {
            if (ignoreWatcherChanges)
                return;

            Debug.WriteLine($"WATCHER: {e.ChangeType} -> {e.FullPath}");

            watcherCts?.Cancel();
            watcherCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, watcherCts.Token);

                    await DispatcherQueue.EnqueueAsync(async () =>
                    {
                        await LoadResourcePacks();
                    });
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        private async void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingResourcePacks)
                return;

            if (sender is not ToggleSwitch toggle)
                return;

            if (toggle.DataContext is not ManagedFileItem resourcePack)
                return;

            ignoreWatcherChanges = true;

            try
            {
                if (toggle.IsOn)
                {
                    if (resourcePack.FilePath.EndsWith(".disabled"))
                    {
                        var enabledPath = resourcePack.FilePath.Replace(".zip.disabled", ".zip");
                        File.Move(resourcePack.FilePath, enabledPath);
                        resourcePack.FilePath = enabledPath;
                    }
                }
                else
                {
                    if (!resourcePack.FilePath.EndsWith(".disabled"))
                    {
                        var disabledPath = resourcePack.FilePath + ".disabled";
                        File.Move(resourcePack.FilePath, disabledPath);
                        resourcePack.FilePath = disabledPath;
                    }
                }

                resourcePack.IsEnabled = toggle.IsOn;
            }
            catch
            {
                toggle.IsOn = !toggle.IsOn;
            }

            await Task.Delay(300);
            ignoreWatcherChanges = false;
        }

        private async void DeleteResourcePack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ManagedFileItem resourcePack)
                return;

            ignoreWatcherChanges = true;

            try
            {
                if (File.Exists(resourcePack.FilePath))
                {
                    File.Delete(resourcePack.FilePath);
                    ResourcePacks.Remove(resourcePack);
                    resourcePacks.Remove(resourcePack);
                }
            }
            catch (Exception ex)
            {
                ResourcePacksErrorText.Text = ex.Message;
                ResourcePacksErrorText.Visibility = Visibility.Visible;
            }

            await Task.Delay(500);
            ignoreWatcherChanges = false;
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ManagedFileItem resourcePack)
                return;

            Process.Start("explorer.exe", $"/select,\"{resourcePack.FilePath}\"");
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ResourcePacksFolder,
                UseShellExecute = true
            });
        }

        private async void OpenModrinth_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ManagedFileItem resourcePack)
                return;

            try
            {
                var result = (await ModrinthHelper.SearchProjectsAsync(ModrinthProjectKind.ResourcePack, resourcePack.Name, 1))
                    .FirstOrDefault();

                if (result == null)
                    return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://modrinth.com/resourcepack/{result.Slug}",
                    UseShellExecute = true
                });
            }
            catch
            {
                ResourcePacksErrorText.Visibility = Visibility.Visible;
            }
        }

        private void ResourcePack_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

        private async void ResourcePack_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            Directory.CreateDirectory(ResourcePacksFolder);
            ignoreWatcherChanges = true;

            foreach (var item in items)
            {
                if (item is Windows.Storage.StorageFile file)
                {
                    if (!file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var destination = Path.Combine(ResourcePacksFolder, file.Name);
                    File.Copy(file.Path, destination, true);
                }
            }

            await LoadResourcePacks();
            await Task.Delay(500);
            ignoreWatcherChanges = false;
        }

        private void ResourcePacksSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            var query = textBox.Text.Trim();
            var filteredResourcePacks = string.IsNullOrWhiteSpace(query)
                ? resourcePacks.OrderBy(r => r.Name).ToList()
                : resourcePacks
                    .Where(resourcePack => resourcePack.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(resourcePack => resourcePack.Name)
                    .ToList();

            SyncResourcePacks(filteredResourcePacks);
        }

        private async Task LoadResourcePacks()
        {
            isLoadingResourcePacks = true;

            try
            {
                Directory.CreateDirectory(ResourcePacksFolder);

                var files = Directory.GetFiles(ResourcePacksFolder)
                    .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var loadedResourcePacks = new List<ManagedFileItem>();

                foreach (var file in files)
                {
                    loadedResourcePacks.Add(await ModrinthHelper.ReadManagedFileAsync(file, ModrinthProjectKind.ResourcePack));
                }

                resourcePacks = loadedResourcePacks.OrderBy(r => r.Name).ToList();
                SyncResourcePacks(resourcePacks);
            }
            finally
            {
                isLoadingResourcePacks = false;
            }
        }

        private void SyncResourcePacks(List<ManagedFileItem> newResourcePacks)
        {
            for (int i = ResourcePacks.Count - 1; i >= 0; i--)
            {
                var existing = ResourcePacks[i];
                bool shouldExist = newResourcePacks.Any(r => r.FilePath == existing.FilePath);

                if (!shouldExist)
                    ResourcePacks.RemoveAt(i);
            }

            for (int i = 0; i < newResourcePacks.Count; i++)
            {
                var resourcePack = newResourcePacks[i];
                bool alreadyExists = ResourcePacks.Any(r => r.FilePath == resourcePack.FilePath);

                if (!alreadyExists)
                    ResourcePacks.Insert(i, resourcePack);
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await LoadResourcePacks();
        }
    }
}
