using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;
using File = System.IO.File;


namespace Yorii_Launcher.Pages
{
    public sealed partial class InstalledModsPage : Page
    {
        private readonly ObservableCollection<ModItem> Mods = [];
        private List<ModItem> mods = [];
        private FileSystemWatcher? modsWatcher;
        private bool isLoadingMods;
        private bool ignoreWatcherChanges;

        public InstalledModsPage()
        {
            InitializeComponent();

            this.NavigationCacheMode = NavigationCacheMode.Required;
            ModsList.ItemsSource = Mods;
            var savedMode = (PluginViewMode)SettingsManager.Current.InstalledModsViewMode;
            PluginViewModeHelper.Apply(ModsList, savedMode);
            ModsViewModeSegmented.SelectedIndex = (int)savedMode;

            _ = LoadMods();

            StartModsWatcher();

            MemoryOptimizer.ReduceMemory();
        }

        private void ViewModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PluginViewModeHelper.ApplyFromSelectedIndex(ModsList, ModsViewModeSegmented.SelectedIndex);
            SettingsManager.Current.InstalledModsViewMode = ModsViewModeSegmented.SelectedIndex;
            SettingsManager.SaveSettings();
        }

        // watch mods folder for changes and refresh the list
        private void StartModsWatcher()
        {
            var minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();
            if (string.IsNullOrWhiteSpace(minecraftPath)) return;

            var modsFolder = Path.Combine(minecraftPath, "mods");
            Directory.CreateDirectory(modsFolder);

            modsWatcher?.Dispose();
            watcherCts?.Cancel();
            watcherCts?.Dispose();

            modsWatcher = new FileSystemWatcher(modsFolder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                Filter = "*.jar*",
                IncludeSubdirectories = false,
                InternalBufferSize = 32768,
                EnableRaisingEvents = true
            };
            modsWatcher.Created += ModsChanged;
            modsWatcher.Changed += ModsChanged;
            modsWatcher.Deleted += ModsChanged;
            modsWatcher.Renamed += ModsChanged;
            modsWatcher.Error += (_, _) => StartModsWatcher();
        }

        private CancellationTokenSource? watcherCts;
        private void ModsChanged(object sender, FileSystemEventArgs e)
        {
            if (ignoreWatcherChanges) return;

            watcherCts?.Cancel();
            watcherCts?.Dispose();
            watcherCts = new CancellationTokenSource();
            var token = watcherCts.Token;

            _ = Task.Delay(350, token).ContinueWith(async _ =>
            {
                if (token.IsCancellationRequested) return;
                DispatcherQueue.TryEnqueue(async () => await LoadMods());
            }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        }

        // rename .jar to .jar.disabled to enable/disable
        private async void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (isLoadingMods)
                return;

            if (sender is not ToggleSwitch toggle)
                return;

            if (toggle.DataContext is not ModItem mod)
                return;

            ignoreWatcherChanges = true;

            try
            {
                // enable
                if (toggle.IsOn)
                {
                    if (mod.FilePath.EndsWith(".disabled"))
                    {
                        var enabledPath =
                            mod.FilePath.Replace(".jar.disabled", ".jar");

                        File.Move(mod.FilePath, enabledPath);

                        mod.FilePath = enabledPath;
                    }
                }
                // disable
                else
                {
                    if (!mod.FilePath.EndsWith(".disabled"))
                    {
                        var disabledPath = mod.FilePath + ".disabled";

                        File.Move(mod.FilePath, disabledPath);
                        mod.FilePath = disabledPath;
                    }
                }

                mod.IsEnabled = toggle.IsOn;
            }
            catch
            {
                toggle.IsOn = !toggle.IsOn;
            }

            await Task.Delay(300);

            ignoreWatcherChanges = false;
        }

        // delete mod file
        private async void DeleteMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ModItem mod)
                return;

            ignoreWatcherChanges = true;

            try
            {
                if (File.Exists(mod.FilePath))
                {
                    File.Delete(mod.FilePath);

                    Mods.Remove(mod);

                    mods.Remove(mod);
                }
            }
            catch (Exception ex)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Error",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                    Background = DialogHelper.GetAcrylicBrush(),
                    RequestedTheme = ThemeHelper.GetCurrentTheme(),
                    XamlRoot = XamlRoot
                };
                errorDialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
                await errorDialog.ShowAsync();
                MemoryOptimizer.ReduceMemory();
            }

            await Task.Delay(500);

            ignoreWatcherChanges = false;
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ModItem mod)
                return;

            Process.Start(
                "explorer.exe",
                $"/select,\"{mod.FilePath}\"");
        }

        // find slug on modrinth then open in browser
        private async void OpenModrinth_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item)
                return;

            if (item.DataContext is not ModItem mod)
                return;

            if (!string.IsNullOrWhiteSpace(
                mod.CachedModrinthSlug))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName =
                        $"https://modrinth.com/mod/{mod.CachedModrinthSlug}",
                    UseShellExecute = true
                });

                return;
            }

            try
            {
                var query = Uri.EscapeDataString($"{mod.Name} {mod.ModId}");
                var apiUrl = $"https://api.modrinth.com/v2/search?query={query}&facets=[[\"categories:fabric\"]]";
                var json = await HttpService.Client.GetStringAsync(apiUrl);

                using JsonDocument doc = JsonDocument.Parse(json);
                var hits = doc.RootElement.GetProperty("hits");

                if (hits.GetArrayLength() == 0)
                    return;

                var slug = hits[0].GetProperty("slug").GetString();

                mod.CachedModrinthSlug = slug;

                Process.Start(new ProcessStartInfo
                {
                    FileName =
                        $"https://modrinth.com/mod/{slug}",
                    UseShellExecute = true
                });
            }
            catch
            {
                ModsErrorText.Visibility = Visibility.Visible;
            }
        }

        private void Mod_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation =
                Windows.ApplicationModel.DataTransfer
                .DataPackageOperation.Copy;
        }

        // drag jar files into mods folder
        private async void Mod_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(
                Windows.ApplicationModel.DataTransfer
                .StandardDataFormats.StorageItems))
            {
                return;
            }

            var items =
                await e.DataView.GetStorageItemsAsync();

            var minecraftPath =
                SettingsManager.Current.GetActiveMinecraftPath();

            if (string.IsNullOrWhiteSpace(minecraftPath))
                return;

            var modsFolder =
                Path.Combine(minecraftPath, "mods");

            Directory.CreateDirectory(modsFolder);

            ignoreWatcherChanges = true;

            foreach (var item in items)
            {
                if (item is Windows.Storage.StorageFile file)
                {
                    if (!file.Name.EndsWith(".jar"))
                        continue;

                    var destination =
                        Path.Combine(
                            modsFolder,
                            file.Name);

                    File.Copy(
                        file.Path,
                        destination,
                        true);
                }
            }

            await LoadMods();

            await Task.Delay(500);

            ignoreWatcherChanges = false;
        }


        private void ModsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            var query = textBox.Text.Trim();
            ApplyFilter(query);
        }

        private void ApplyFilter(string query)
        {
            List<ModItem> filteredMods;

            if (string.IsNullOrWhiteSpace(query))
            {
                filteredMods = mods.OrderBy(m => m.Name).ToList();
            }
            else
            {
                filteredMods = mods
                    .Where(mod =>
                        mod.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                        || mod.Version.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(m => m.Name)
                    .ToList();
            }

            for (int i = Mods.Count - 1; i >= 0; i--)
            {
                var existing = Mods[i];
                if (!filteredMods.Any(m => m.FilePath == existing.FilePath))
                    Mods.RemoveAt(i);
            }

            for (int i = 0; i < filteredMods.Count; i++)
            {
                var mod = filteredMods[i];
                if (!Mods.Any(m => m.FilePath == mod.FilePath))
                    Mods.Insert(i, mod);
            }
        }

        // load mods from disk, update observable collection
        private async Task LoadMods()
        {
            isLoadingMods = true;

            try
            {
                var path = SettingsManager.Current.GetActiveMinecraftPath();

                if (string.IsNullOrWhiteSpace(path))
                    return;

                var loadedMods =
                    (await ModHelper.GetInstalledMods(path))
                    .OrderBy(m => m.Name)
                    .ToList();

                mods = loadedMods;

                // remove missing
                for (int i = Mods.Count - 1; i >= 0; i--)
                {
                    var existing = Mods[i];

                    bool stillExists =
                        loadedMods.Any(m =>
                            m.FilePath == existing.FilePath);

                    if (!stillExists)
                    {
                        Mods.RemoveAt(i);
                    }
                }

                // add new
                for (int i = 0; i < loadedMods.Count; i++)
                {
                    var mod = loadedMods[i];

                    bool exists =
                        Mods.Any(m =>
                            m.FilePath == mod.FilePath);

                    if (!exists)
                    {
                        Mods.Insert(i, mod);
                    }
                }
            }
            finally
            {
                isLoadingMods = false;
            }
        }
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // re-point the watcher at the active instance (the page is cached, so
            // the folder can change between visits) then always refresh the list
            StartModsWatcher();
            await LoadMods(); // always refresh when page is shown
        }
    }
}