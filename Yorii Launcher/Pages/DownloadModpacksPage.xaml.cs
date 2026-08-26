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
    public sealed partial class DownloadModpacksPage : Page
    {
        private readonly ObservableCollection<OnlineModItem> OnlineModpacks = [];
        private CancellationTokenSource? searchCts;

        public DownloadModpacksPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            ModpacksList.ItemsSource = OnlineModpacks;
            var savedMode = (PluginViewMode)SettingsManager.Current.DownloadModpacksViewMode;
            PluginViewModeHelper.Apply(ModpacksList, savedMode);
            ModpacksViewModeSegmented.SelectedIndex = (int)savedMode;

            _ = LoadFeaturedModpacks();
            MemoryOptimizer.ReduceMemory();
        }

        private void ViewModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PluginViewModeHelper.ApplyFromSelectedIndex(ModpacksList, ModpacksViewModeSegmented.SelectedIndex);
            SettingsManager.Current.DownloadModpacksViewMode = ModpacksViewModeSegmented.SelectedIndex;
            SettingsManager.SaveSettings();
        }

        // search modrinth for modpacks matching the query
        private async void ModpackSearchBox_TextChanged(object sender, TextChangedEventArgs e)
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
                await LoadFeaturedModpacks();
                return;
            }

            try
            {
                ModpacksErrorPanel.Visibility = Visibility.Collapsed;
                var newModpacks = await ModrinthHelper.SearchProjectsAsync(ModrinthProjectKind.Modpack, query);
                SyncItems(newModpacks);
            }
            catch
            {
                ModpacksErrorPanel.Visibility = Visibility.Visible;
            }
        }

        private async void InstallModpack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not OnlineModItem modpack)
                return;

            try
            {
                if (ModrinthHelper.IsAlreadyInstalled(ModrinthProjectKind.Modpack, modpack.Slug))
                {
                    NotificationHelper.Show("Modpack already installed", $"'{modpack.Title}' is already installed.");
                    return;
                }

                await ModrinthHelper.InstallLatestProjectAsync(ModrinthProjectKind.Modpack, modpack.Slug, modpack.Title, modpack.Icon);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error($"Modpack download failed ({modpack.Title}): {ex.Message}");

                if (IsNetworkError(ex))
                    NotificationHelper.Show("Modpack download failed", $"Could not reach Modrinth. Check your internet connection.");
                else
                    NotificationHelper.Show("Modpack download failed", $"Could not download {modpack.Title}.");
            }
        }

        private void OpenOnlineModpack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not OnlineModItem modpack)
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://modrinth.com/modpack/{modpack.Slug}",
                UseShellExecute = true
            });
        }

        private async void ShowVersions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not OnlineModItem modpack)
                return;

            var versions = await ModrinthHelper.GetVersionsAsync(ModrinthProjectKind.Modpack, modpack.Slug);
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
                    try
                    {
                        if (ModrinthHelper.IsAlreadyInstalled(ModrinthProjectKind.Modpack, modpack.Slug))
                        {
                            NotificationHelper.Show("Modpack already installed", $"'{modpack.Title}' is already installed.");
                            return;
                        }

                        await ModrinthHelper.InstallVersionAsync(ModrinthProjectKind.Modpack, version.VersionId, modpack.Title, modpack.Icon);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Modpack download failed ({modpack.Title}): {ex.Message}");

                        if (IsNetworkError(ex))
                            NotificationHelper.Show("Modpack download failed", $"Could not reach Modrinth. Check your internet connection.");
                        else
                            NotificationHelper.Show("Modpack download failed", $"Could not download {modpack.Title}.");
                    }
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

        private async void ModpacksRetryButton_Click(object sender, RoutedEventArgs e)
        {
            ModpacksErrorPanel.Visibility = Visibility.Collapsed;
            await LoadFeaturedModpacks();
        }

        private async Task LoadFeaturedModpacks()
        {
            bool hasInternet = await NetworkHelper.InternetAvailable();
            if (!hasInternet)
            {
                ModpacksErrorPanel.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                ModpacksErrorPanel.Visibility = Visibility.Collapsed;
                var featuredModpacks = new[] { "fabulously-optimized", "simply-optimized", "adrenaline", "additive" };

                var tasks = featuredModpacks.Select(async modpackQuery =>
                {
                    return (await ModrinthHelper.SearchProjectsAsync(ModrinthProjectKind.Modpack, modpackQuery, 1))
                        .FirstOrDefault();
                });

                var results = await Task.WhenAll(tasks);

                OnlineModpacks.Clear();

                foreach (var modpack in results.OfType<OnlineModItem>())
                {
                    OnlineModpacks.Add(modpack);
                }
            }
            catch
            {
                ModpacksErrorPanel.Visibility = Visibility.Visible;
            }
        }

        private void SyncItems(List<OnlineModItem> newModpacks)
        {
            for (int i = OnlineModpacks.Count - 1; i >= 0; i--)
            {
                bool exists = newModpacks.Any(x => x.Slug == OnlineModpacks[i].Slug);
                if (!exists)
                    OnlineModpacks.RemoveAt(i);
            }

            foreach (var modpack in newModpacks)
            {
                bool exists = OnlineModpacks.Any(x => x.Slug == modpack.Slug);
                if (!exists)
                    OnlineModpacks.Add(modpack);
            }
        }

        private static bool IsNetworkError(Exception ex) => ex is
            System.Net.Http.HttpRequestException
            or System.Net.Sockets.SocketException
            or System.IO.IOException;
    }
}