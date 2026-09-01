using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Pages;

public sealed partial class DiscoverThemesPage : Page
{
    private readonly ThemeMarketplaceService service = new();
    private readonly ObservableCollection<ThemeMarketplaceItem> themes = [];
    private readonly Dictionary<string, ThemeMarketplaceItem> itemCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim previewSemaphore = new(6);

    private IReadOnlyList<ThemeCatalogEntry> catalog = [];
    private HashSet<string> installedNames = [];
    private CancellationTokenSource? previewCts;
    private CancellationTokenSource? searchDebounceCts;

    public DiscoverThemesPage()
    {
        InitializeComponent();
        ThemeList.ItemsSource = themes;
        ThemeList.ContainerContentChanging += ThemeList_ContainerContentChanging;
        PluginViewModeHelper.Apply(ThemeList, PluginViewMode.Grid);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        MemoryOptimizer.ReduceMemory();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        previewCts?.Cancel();
        previewCts?.Dispose();
        previewCts = null;
        searchDebounceCts?.Cancel();
        searchDebounceCts?.Dispose();
        searchDebounceCts = null;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadCatalogAsync();
    }

    private async Task LoadCatalogAsync()
    {
        LoadingRing.Visibility = Visibility.Visible;
        ThemesErrorPanel.Visibility = Visibility.Collapsed;
        ThemeList.Visibility = Visibility.Collapsed;

        try
        {
            catalog = await service.GetCatalogCachedAsync();
            installedNames = GetInstalledThemeNames();
            ApplyFilter(SearchBox.Text.Trim());

            foreach (var item in themes)
                item.PreviewRequested = false;

            previewCts?.Cancel();
            previewCts = new CancellationTokenSource();

            ThemeList.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            if (themes.Count == 0)
                ThemesErrorPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private static HashSet<string> GetInstalledThemeNames() => InstalledThemes.GetNames();

    private void ThemeList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not ThemeMarketplaceItem item)
            return;

        if (item.PreviewImage is null && !item.PreviewRequested)
        {
            item.PreviewRequested = true;
            _ = LoadPreviewAsync(item);
        }
    }

    private async Task LoadPreviewAsync(ThemeMarketplaceItem item)
    {
        if (previewCts is null || previewCts.IsCancellationRequested)
            previewCts = new CancellationTokenSource();

        var token = previewCts.Token;

        try
        {
            await previewSemaphore.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var image = await service.GetPreviewImageAsync(item.Entry, token);

            if (image is not null && !token.IsCancellationRequested)
                DispatcherQueue.TryEnqueue(() => item.PreviewImage = image);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            previewSemaphore.Release();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        var query = textBox.Text.Trim();
        searchDebounceCts?.Cancel();
        searchDebounceCts = new CancellationTokenSource();
        var token = searchDebounceCts.Token;

        _ = DebounceFilterAsync(query, token);
    }

    private async Task DebounceFilterAsync(string query, CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ApplyFilter(query);
    }

    private void ApplyFilter(string query)
    {
        var activeFolder = ThemeManager.Current.ActiveThemeFolder;
        var filtered = string.IsNullOrWhiteSpace(query)
            ? catalog
            : catalog.Where(e => e.Theme.Contains(query, StringComparison.OrdinalIgnoreCase)
                              || e.Author.Contains(query, StringComparison.OrdinalIgnoreCase));

        themes.Clear();
        foreach (var entry in filtered)
        {
            var safeName = GetSafeDirectoryName(entry.Theme);
            var isInstalled = installedNames.Contains(safeName);

            if (!itemCache.TryGetValue(safeName, out var item))
            {
                item = new ThemeMarketplaceItem
                {
                    ThemeName = entry.Theme,
                    Author = entry.Author,
                    Entry = entry
                };
                itemCache[safeName] = item;
            }

            item.IsInstalled = isInstalled;
            item.IsActive = isInstalled && string.Equals(safeName, activeFolder, StringComparison.OrdinalIgnoreCase);
            themes.Add(item);
        }
    }

    private ThemeSettings? previousThemeSettings;

    private async void InstallTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ThemeMarketplaceItem item)
            return;

        button.IsEnabled = false;
        button.Content = "Installing";

        try
        {
            previousThemeSettings = CloneSettings(ThemeManager.Current);
            await service.InstallAsync(item.Entry);
            item.IsInstalled = true;
            installedNames.Add(GetSafeDirectoryName(item.ThemeName));
        }
        catch (Exception ex)
        {
            NotificationHelper.Show("Install failed", ex.Message);
        }
        finally
        {
            button.Content = "Install";
            button.IsEnabled = true;
        }
    }

    private void ApplyTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ThemeMarketplaceItem item)
            return;

        try
        {
            var safeName = GetSafeDirectoryName(item.ThemeName);
            var themeDir = Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                "Themes",
                safeName);

            service.ApplyTheme(themeDir);

            foreach (var t in themes)
                t.IsActive = false;

            item.IsActive = true;
        }
        catch (Exception ex)
        {
            NotificationHelper.Show("Apply failed", ex.Message);
        }
    }

    private void DeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ThemeMarketplaceItem item)
            return;

        try
        {
            var safeName = GetSafeDirectoryName(item.ThemeName);
            var themeDir = Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                "Themes",
                safeName);

            if (Directory.Exists(themeDir))
                Directory.Delete(themeDir, true);

            var wasActive = item.IsActive;

            item.IsInstalled = false;
            item.IsActive = false;
            installedNames.Remove(safeName);

            if (wasActive)
            {
                ThemeManager.Current.ActiveThemeFolder = "";
                ThemeManager.SaveSettings();

                if (previousThemeSettings is not null)
                {
                    ThemeManager.Current.CurrentTheme = previousThemeSettings.CurrentTheme;
                    ThemeManager.Current.BackgroundImagePath = previousThemeSettings.BackgroundImagePath;
                    ThemeManager.Current.OverlayOpacity = previousThemeSettings.OverlayOpacity;
                    ThemeManager.Current.OverlayBlurEnabled = previousThemeSettings.OverlayBlurEnabled;
                    ThemeManager.Current.UseCustomAccentColor = previousThemeSettings.UseCustomAccentColor;
                    ThemeManager.Current.CustomAccentColor = previousThemeSettings.CustomAccentColor;
                    ThemeManager.Current.ServerlistEnabled = previousThemeSettings.ServerlistEnabled;
                    ThemeManager.Current.WorldlistEnabled = previousThemeSettings.WorldlistEnabled;
                    ThemeManager.Current.ReleasenotesEnabled = previousThemeSettings.ReleasenotesEnabled;
                    ThemeManager.Current.CardBorderThickness = previousThemeSettings.CardBorderThickness;
                    ThemeManager.Current.CardBorderColor = previousThemeSettings.CardBorderColor;
                    ThemeManager.Current.CardBackgroundColor = previousThemeSettings.CardBackgroundColor;
                    ThemeManager.Current.SettingscardBackgroundColor = previousThemeSettings.SettingscardBackgroundColor;
                    ThemeManager.Current.SettingsexpanderHoverColor = previousThemeSettings.SettingsexpanderHoverColor;
                    ThemeManager.Current.SettingsexpanderPressedColor = previousThemeSettings.SettingsexpanderPressedColor;
                    ThemeManager.Current.SettingscardDisabledColor = previousThemeSettings.SettingscardDisabledColor;
                    ThemeManager.Current.Systembackdrop = previousThemeSettings.Systembackdrop;
                    ThemeManager.SaveSettings();
                }

                ThemeHelper.ApplySavedTheme();
                AccentThemeManager.ApplySavedAccent();
                MainWindow.Instance?.ApplyBackgroundSettings();
            }
        }
        catch (Exception ex)
        {
            NotificationHelper.Show("Delete failed", ex.Message);
        }
    }

    private static ThemeSettings CloneSettings(ThemeSettings src) => new()
    {
        CurrentTheme = src.CurrentTheme,
        BackgroundImagePath = src.BackgroundImagePath,
        OverlayOpacity = src.OverlayOpacity,
        OverlayBlurEnabled = src.OverlayBlurEnabled,
        UseCustomAccentColor = src.UseCustomAccentColor,
        CustomAccentColor = src.CustomAccentColor
    };

    private async void ThemesRetryButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadCatalogAsync();
    }

    private static string GetSafeDirectoryName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(name.Trim().Select(character =>
            Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character));
        return string.IsNullOrWhiteSpace(safeName) ? "unnamed-theme" : safeName;
    }
}