using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;


namespace Yorii_Launcher.Pages
{
    public sealed partial class DownloadModsPage : Page
    {
        private readonly ObservableCollection<OnlineModItem> OnlineMods = [];
        private CancellationTokenSource? searchCts;
        public DownloadModsPage()
        {
            InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;

            ModrinthList.ItemsSource = OnlineMods;
            var savedMode = (PluginViewMode)SettingsManager.Current.DownloadModsViewMode;
            PluginViewModeHelper.Apply(ModrinthList, savedMode);
            ModrinthViewModeSegmented.SelectedIndex = (int)savedMode;

            _ = LoadFeaturedMods();
            MemoryOptimizer.ReduceMemory();
        }

        private void ViewModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PluginViewModeHelper.ApplyFromSelectedIndex(ModrinthList, ModrinthViewModeSegmented.SelectedIndex);
            SettingsManager.Current.DownloadModsViewMode = ModrinthViewModeSegmented.SelectedIndex;
            SettingsManager.SaveSettings();
        }

        // search modrinth for mods matching the query
        private async void ModrinthSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            var query = textBox.Text.Trim();

            searchCts?.Cancel();
            searchCts = new CancellationTokenSource();
            var token = searchCts.Token;

            try
            {
                await Task.Delay(300, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                await LoadFeaturedMods();
                return;
            }

            try
            {
                ModsErrorPanel.Visibility = Visibility.Collapsed;
                var newMods = await ModrinthHelper.SearchProjectsAsync(ModrinthProjectKind.Mod, query);

                // REMOVE OLD ITEMS
                for (int i = OnlineMods.Count - 1; i >= 0; i--)
                {
                    bool exists =
                        newMods.Any(x =>
                            x.Slug == OnlineMods[i].Slug);

                    if (!exists)
                    {
                        OnlineMods.RemoveAt(i);
                    }
                }

                // ADD NEW ITEMS
                foreach (var mod in newMods)
                {
                    bool exists =
                        OnlineMods.Any(x =>
                            x.Slug == mod.Slug);

                    if (!exists)
                    {
                        OnlineMods.Add(mod);
                    }
                }
            }
            catch
            {
                ModsErrorPanel.Visibility = Visibility.Visible;
            }
        }

        private async void InstallMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not OnlineModItem mod)
                return;

            try
            {
                await ModrinthHelper.InstallLatestProjectAsync(ModrinthProjectKind.Mod, mod.Slug);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                NotificationHelper.Show("Mod install failed", $"Could not install {mod.Title}. Check your internet connection.");
            }
        }

        private void OpenOnlineMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not OnlineModItem mod)
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://modrinth.com/mod/{mod.Slug}",

                UseShellExecute = true
            });
        }

        // show available versions in flyout
        private async void ShowVersions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not OnlineModItem mod)
                return;

            var versions = await GetVersions(mod.Slug);

            var flyout = new MenuFlyout();

            foreach (var version in versions)
            {
                var item = new MenuFlyoutItem
                {
                    Text = version.VersionName,
                    Tag = version
                };

                item.Click += async (_, __) =>
                {
                    await ModrinthHelper.InstallVersionAsync(ModrinthProjectKind.Mod, version.VersionId);
                };

                flyout.Items.Add(item);
            }

            if (versions.Count == 0)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = "No downloadable versions found",
                    IsEnabled = false
                });
            }

            flyout.ShowAt(button, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.Bottom
            });
        }

        // fetch compatible versions from modrinth
        private static async Task<List<OnlineModItem>> GetVersions(string slug)
        {
            return await ModrinthHelper.GetVersionsAsync(ModrinthProjectKind.Mod, slug);
        }


        private async void ModsRetryButton_Click(object sender, RoutedEventArgs e)
        {
            ModsErrorPanel.Visibility = Visibility.Collapsed;
            await LoadFeaturedMods();
        }

        // load a set of popular mods as defaults
        private async Task LoadFeaturedMods()
        {
            bool hasInternet = await NetworkHelper.InternetAvailable();
            if (hasInternet)
            {
                try
                {
                    ModsErrorPanel.Visibility = Visibility.Collapsed;
                    var featuredMods = new[] { "sodium", "lithium", "ferritecore", "fabric-api", "immediatelyfast", "appleskin", "dark-loading-screen" };

                    var tasks = featuredMods.Select(async modQuery =>
                    {
                        return (await ModrinthHelper.SearchProjectsAsync(ModrinthProjectKind.Mod, modQuery, 1))
                            .FirstOrDefault();
                    });

                    var results = await Task.WhenAll(tasks);

                    OnlineMods.Clear();

                    foreach (var mod in results.OfType<OnlineModItem>())
                    {
                        OnlineMods.Add(mod);
                    }
                }
                catch
                {
                    ModsErrorPanel.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ModsErrorPanel.Visibility = Visibility.Visible;
            }

        }
    }

}
