using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    public sealed partial class InstalledModpacksPage : Page
    {
        private readonly ObservableCollection<ManagedFileItem> Modpacks = [];
        private List<ManagedFileItem> modpacks = [];
        private FileSystemWatcher? modpacksWatcher;
        private CancellationTokenSource? watcherCts;
        private bool isLoadingModpacks;
        private bool ignoreWatcherChanges;

        public InstalledModpacksPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            ModpacksList.ItemsSource = Modpacks;
            var savedMode = (PluginViewMode)SettingsManager.Current.InstalledModpacksViewMode;
            PluginViewModeHelper.Apply(ModpacksList, savedMode);
            ModpacksViewModeSegmented.SelectedIndex = (int)savedMode;

            _ = LoadModpacks();
            StartModpacksWatcher();

            MemoryOptimizer.ReduceMemory();
        }

        private void ViewModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PluginViewModeHelper.ApplyFromSelectedIndex(ModpacksList, ModpacksViewModeSegmented.SelectedIndex);
            SettingsManager.Current.InstalledModpacksViewMode = ModpacksViewModeSegmented.SelectedIndex;
            SettingsManager.SaveSettings();
        }

        private string ModpacksFolder
        {
            get
            {
                var minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();
                return Path.Combine(minecraftPath, "modpacks");
            }
        }

        private void StartModpacksWatcher()
        {
            Directory.CreateDirectory(ModpacksFolder);

            modpacksWatcher?.Dispose();
            modpacksWatcher = new FileSystemWatcher(ModpacksFolder);
            modpacksWatcher.Created += ModpacksChanged;
            modpacksWatcher.Deleted += ModpacksChanged;
            modpacksWatcher.Renamed += ModpacksChanged;
            modpacksWatcher.EnableRaisingEvents = true;
        }

        private void ModpacksChanged(object sender, FileSystemEventArgs e)
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
                        await LoadModpacks();
                    });
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        private async void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingModpacks)
                return;

            if (sender is not ToggleSwitch toggle)
                return;

            if (toggle.DataContext is not ManagedFileItem modpack)
                return;

            ignoreWatcherChanges = true;

            try
            {
                if (toggle.IsOn)
                {
                    if (modpack.FilePath.EndsWith(".disabled"))
                    {
                        var enabledPath = modpack.FilePath.Replace(".mrpack.disabled", ".mrpack");
                        File.Move(modpack.FilePath, enabledPath);
                        modpack.FilePath = enabledPath;
                    }
                }
                else
                {
                    if (!modpack.FilePath.EndsWith(".disabled"))
                    {
                        var disabledPath = modpack.FilePath + ".disabled";
                        File.Move(modpack.FilePath, disabledPath);
                        modpack.FilePath = disabledPath;
                    }
                }

                modpack.IsEnabled = toggle.IsOn;
            }
            catch
            {
                toggle.IsOn = !toggle.IsOn;
            }

            await Task.Delay(300);
            ignoreWatcherChanges = false;
        }

        private async void DeleteModpack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ManagedFileItem modpack)
                return;

            ignoreWatcherChanges = true;

            try
            {
                if (File.Exists(modpack.FilePath))
                {
                    File.Delete(modpack.FilePath);
                    Modpacks.Remove(modpack);
                    modpacks.Remove(modpack);
                }
            }
            catch (Exception ex)
            {
                ModpacksErrorText.Text = ex.Message;
                ModpacksErrorText.Visibility = Visibility.Visible;
            }

            await Task.Delay(500);
            ignoreWatcherChanges = false;
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ManagedFileItem modpack)
                return;

            Process.Start("explorer.exe", $"/select,\"{modpack.FilePath}\"");
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ModpacksFolder,
                UseShellExecute = true
            });
        }

        private async void OpenModrinth_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ManagedFileItem modpack)
                return;

            try
            {
                var result = (await ModrinthHelper.SearchProjectsAsync(ModrinthProjectKind.Modpack, modpack.Name, 1))
                    .FirstOrDefault();

                if (result == null)
                    return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://modrinth.com/modpack/{result.Slug}",
                    UseShellExecute = true
                });
            }
            catch
            {
                ModpacksErrorText.Visibility = Visibility.Visible;
            }
        }

        private void Modpack_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }

        private async void Modpack_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            Directory.CreateDirectory(ModpacksFolder);
            ignoreWatcherChanges = true;

            foreach (var item in items)
            {
                if (item is Windows.Storage.StorageFile file)
                {
                    if (!file.Name.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var destination = Path.Combine(ModpacksFolder, file.Name);
                    File.Copy(file.Path, destination, true);
                }
            }

            await LoadModpacks();
            await Task.Delay(500);
            ignoreWatcherChanges = false;
        }

        private void ModpacksSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            var query = textBox.Text.Trim();
            var filteredModpacks = string.IsNullOrWhiteSpace(query)
                ? modpacks.OrderBy(m => m.Name).ToList()
                : modpacks
                    .Where(modpack => modpack.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(modpack => modpack.Name)
                    .ToList();

            SyncModpacks(filteredModpacks);
        }

        private async Task LoadModpacks()
        {
            isLoadingModpacks = true;

            try
            {
                Directory.CreateDirectory(ModpacksFolder);

                var files = Directory.GetFiles(ModpacksFolder)
                    .Where(f => f.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mrpack.disabled", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var loadedModpacks = new List<ManagedFileItem>();

                foreach (var file in files)
                {
                    loadedModpacks.Add(await ModrinthHelper.ReadManagedFileAsync(file, ModrinthProjectKind.Modpack));
                }

                modpacks = loadedModpacks.OrderBy(m => m.Name).ToList();
                SyncModpacks(modpacks);
            }
            finally
            {
                isLoadingModpacks = false;
            }
        }

        private void SyncModpacks(List<ManagedFileItem> newModpacks)
        {
            for (int i = Modpacks.Count - 1; i >= 0; i--)
            {
                var existing = Modpacks[i];
                bool shouldExist = newModpacks.Any(m => m.FilePath == existing.FilePath);

                if (!shouldExist)
                    Modpacks.RemoveAt(i);
            }

            for (int i = 0; i < newModpacks.Count; i++)
            {
                var modpack = newModpacks[i];
                bool alreadyExists = Modpacks.Any(m => m.FilePath == modpack.FilePath);

                if (!alreadyExists)
                    Modpacks.Insert(i, modpack);
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await LoadModpacks();
        }
    }
}
