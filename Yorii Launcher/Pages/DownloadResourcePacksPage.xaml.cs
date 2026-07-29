using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Yorii_Launcher.Pages
{
    public sealed partial class DownloadResourcePacksPage : Page
    {
        private readonly ObservableCollection<OnlineModItem> OnlineResourcePacks = [];
        private CancellationTokenSource? searchCts;

        public DownloadResourcePacksPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            ResourcePacksList.ItemsSource = OnlineResourcePacks;
            var savedMode = (PluginViewMode)SettingsManager.Current.DownloadResourcePacksViewMode;
            PluginViewModeHelper.Apply(ResourcePacksList, savedMode);
            ResourcePacksViewModeSegmented.SelectedIndex = (int)savedMode;

            _ = LoadFeaturedResourcePacks();
            MemoryOptimizer.ReduceMemory();
        }

        private void ViewModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PluginViewModeHelper.ApplyFromSelectedIndex(ResourcePacksList, ResourcePacksViewModeSegmented.SelectedIndex);
            SettingsManager.Current.DownloadResourcePacksViewMode = ResourcePacksViewModeSegmented.SelectedIndex;
            SettingsManager.SaveSettings();
        }

        // search modrinth for resource packs matching the query
        private async void ResourcePackSearchBox_TextChanged(object sender, TextChangedEventArgs e)
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
                await LoadFeaturedResourcePacks();
                return;
            }

            try
            {
                ResourcePacksErrorPanel.Visibility = Visibility.Collapsed;
                var newResourcePacks = await ModrinthHelper.SearchProjectsAsync(ModrinthProjectKind.ResourcePack, query);
                SyncItems(newResourcePacks);
            }
            catch
            {
                ResourcePacksErrorPanel.Visibility = Visibility.Visible;
            }
        }

        private async void InstallResourcePack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not OnlineModItem resourcePack)
                return;

            try
            {
                await ModrinthHelper.InstallLatestProjectAsync(ModrinthProjectKind.ResourcePack, resourcePack.Slug);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                NotificationHelper.Show("Resource pack install failed", $"Could not install {resourcePack.Title}. Check your internet connection.");
            }
        }

        private void OpenOnlineResourcePack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not OnlineModItem resourcePack)
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://modrinth.com/resourcepack/{resourcePack.Slug}",
                UseShellExecute = true
            });
        }

        private async void ShowVersions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not OnlineModItem resourcePack)
                return;

            var versions = await ModrinthHelper.GetVersionsAsync(ModrinthProjectKind.ResourcePack, resourcePack.Slug);
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
                    await ModrinthHelper.InstallVersionAsync(ModrinthProjectKind.ResourcePack, version.VersionId);
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

        private async void ResourcePacksRetryButton_Click(object sender, RoutedEventArgs e)
        {
            ResourcePacksErrorPanel.Visibility = Visibility.Collapsed;
            await LoadFeaturedResourcePacks();
        }

        private async Task LoadFeaturedResourcePacks()
        {
            bool hasInternet = await NetworkHelper.InternetAvailable();
            if (!hasInternet)
            {
                ResourcePacksErrorPanel.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                ResourcePacksErrorPanel.Visibility = Visibility.Collapsed;
                var featuredResourcePacks = new[] { "low-on-fire", "fullbright-ub", "default-dark-mode", "fancy-crops" };

                var tasks = featuredResourcePacks.Select(async resourcePackQuery =>
                {
                    return (await ModrinthHelper.SearchProjectsAsync(ModrinthProjectKind.ResourcePack, resourcePackQuery, 1))
                        .FirstOrDefault();
                });

                var results = await Task.WhenAll(tasks);

                OnlineResourcePacks.Clear();

                foreach (var resourcePack in results.OfType<OnlineModItem>())
                {
                    OnlineResourcePacks.Add(resourcePack);
                }
            }
            catch
            {
                ResourcePacksErrorPanel.Visibility = Visibility.Visible;
            }
        }

        private void SyncItems(List<OnlineModItem> newResourcePacks)
        {
            for (int i = OnlineResourcePacks.Count - 1; i >= 0; i--)
            {
                bool exists = newResourcePacks.Any(x => x.Slug == OnlineResourcePacks[i].Slug);
                if (!exists)
                    OnlineResourcePacks.RemoveAt(i);
            }

            foreach (var resourcePack in newResourcePacks)
            {
                bool exists = OnlineResourcePacks.Any(x => x.Slug == resourcePack.Slug);
                if (!exists)
                    OnlineResourcePacks.Add(resourcePack);
            }
        }
    }
}
