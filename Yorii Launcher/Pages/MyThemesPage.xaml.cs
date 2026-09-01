using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using WinRT.Interop;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Pages;

public sealed partial class MyThemesPage : Page
{
    private readonly ThemeMarketplaceService marketplaceService = new();
    private readonly ThemePublishService publishService = new();
    private readonly ObservableCollection<ThemeMarketplaceItem> themes = [];
    private readonly Dictionary<string, ThemeMarketplaceItem> itemCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim previewSemaphore = new(6);

private IReadOnlyList<ThemeCatalogEntry> catalog = [];
    private HashSet<string> installedNames = [];
    private CancellationTokenSource? previewCts;
    private CancellationTokenSource? searchDebounceCts;
    private ThemeSettings? previousThemeSettings;

    public MyThemesPage()
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
        UpdateLoginState();
        if (ThemePublishService.IsLoggedIn)
            await LoadMyThemesAsync();
    }

    private void UpdateLoginState()
    {
        var loggedIn = ThemePublishService.IsLoggedIn;
        PublishButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        LoginPrompt.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;

        if (!loggedIn)
        {
            LoadingRing.Visibility = Visibility.Collapsed;
            ThemeList.Visibility = Visibility.Collapsed;
            ThemesErrorPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadMyThemesAsync()
    {
        LoadingRing.Visibility = Visibility.Visible;
        ThemesErrorPanel.Visibility = Visibility.Collapsed;
        ThemeList.Visibility = Visibility.Collapsed;

        try
        {
            catalog = await marketplaceService.GetCatalogCachedAsync();
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

    private void ApplyFilter(string query)
    {
        var username = ThemePublishService.CurrentUsername;
        var activeFolder = ThemeManager.Current.ActiveThemeFolder;

        var myThemes = catalog.Where(e =>
            string.Equals(e.Author, username, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query))
        {
            myThemes = myThemes.Where(e =>
                e.Theme.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Author.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        themes.Clear();
        foreach (var entry in myThemes)
        {
            var safeName = GetSafeDirectoryName(entry.Theme);
            var isInstalled = installedNames.Contains(safeName);

            if (!itemCache.TryGetValue(safeName, out var item))
            {
                item = new ThemeMarketplaceItem
                {
                    ThemeName = entry.Theme,
                    Author = entry.Author,
                    Entry = entry,
                    IsOwnTheme = true
                };
                itemCache[safeName] = item;
            }

            item.IsInstalled = isInstalled;
            item.IsActive = isInstalled && string.Equals(safeName, activeFolder, StringComparison.OrdinalIgnoreCase);
            themes.Add(item);
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
            var image = await marketplaceService.GetPreviewImageAsync(item.Entry, token);

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

    private async void PublishButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ThemePublishService.IsLoggedIn)
        {
            NotificationHelper.Show("Not signed in", "Sign in with GitHub to publish themes.");
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Publish theme",
            PrimaryButtonText = "Publish",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Background = DialogHelper.GetAcrylicBrush(),
            RequestedTheme = ThemeHelper.GetCurrentTheme(),
            XamlRoot = XamlRoot
        };

        var nameBox = new TextBox
        {
            PlaceholderText = "Enter theme name",
            Width = 300,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var description = new TextBlock
        {
            Text = "Your current theme settings will be published.",
            FontSize = 13,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap
        };

        var previewImage = new Image
        {
            Height = 120,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            Visibility = Visibility.Collapsed
        };

        var previewBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 8, 0, 0),
            Child = previewImage
        };

        byte[]? selectedImageBytes = null;

        var chooseImageBtn = new Button
        {
            Content = "Choose preview image",
            Margin = new Thickness(0, 8, 0, 0)
        };

        var useCurrentBgCheck = new CheckBox
        {
            Content = "Use current background as preview",
            IsChecked = true,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 13
        };

        var clearImageBtn = new Button
        {
            Content = "Clear",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 12,
            Visibility = Visibility.Collapsed
        };

        var imageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        imageRow.Children.Add(chooseImageBtn);
        imageRow.Children.Add(clearImageBtn);

        chooseImageBtn.Click += async (s, args) =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance!);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                selectedImageBytes = await File.ReadAllBytesAsync(file.Path);
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                using var stream = await file.OpenReadAsync();
                await bitmap.SetSourceAsync(stream);
                previewImage.Source = bitmap;
                previewImage.Visibility = Visibility.Visible;
                previewBorder.Visibility = Visibility.Visible;
                clearImageBtn.Visibility = Visibility.Visible;
                useCurrentBgCheck.IsChecked = false;
            }
        };

        clearImageBtn.Click += (s, args) =>
        {
            selectedImageBytes = null;
            previewImage.Source = null;
            previewImage.Visibility = Visibility.Collapsed;
            previewBorder.Visibility = Visibility.Collapsed;
            clearImageBtn.Visibility = Visibility.Collapsed;
            useCurrentBgCheck.IsChecked = true;
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(description);
        stack.Children.Add(nameBox);
        stack.Children.Add(imageRow);
        stack.Children.Add(previewBorder);
        stack.Children.Add(useCurrentBgCheck);
        dialog.Content = stack;

        dialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        var themeName = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(themeName))
        {
            NotificationHelper.Show("Invalid name", "Theme name cannot be empty.");
            return;
        }

        byte[]? finalImage = null;
        if (selectedImageBytes is not null)
        {
            finalImage = selectedImageBytes;
        }
        else if (useCurrentBgCheck.IsChecked == true)
        {
            finalImage = ThemePublishService.TryReadBackgroundImage();
        }

        await PublishThemeAsync(themeName, finalImage);
    }

    private async Task PublishThemeAsync(string themeName, byte[]? previewImage)
    {
        try
        {
            var definition = ThemePublishService.ExtractCurrentTheme();

            await publishService.PublishThemeAsync(themeName, definition, previewImage);

            NotificationHelper.Show("Theme published", $"'{themeName}' has been published to GitHub.");

            // catalog was updated optimistically by the publish service — just re-filter
            catalog = await marketplaceService.GetCatalogCachedAsync();
            ApplyFilter(SearchBox.Text.Trim());
        }
        catch (Exception ex)
        {
            NotificationHelper.Show("Publish failed", ex.Message);
        }
    }

    private async void InstallTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ThemeMarketplaceItem item)
            return;

        button.IsEnabled = false;
        button.Content = "Installing";

try
        {
            previousThemeSettings = CloneSettings(ThemeManager.Current);
            await marketplaceService.InstallAsync(item.Entry);
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

            marketplaceService.ApplyTheme(themeDir);

            foreach (var t in themes)
                t.IsActive = false;

            item.IsActive = true;
        }
        catch (Exception ex)
        {
            NotificationHelper.Show("Apply failed", ex.Message);
        }
    }

private void UninstallTheme_Click(object sender, RoutedEventArgs e)
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

            NotificationHelper.Show("Theme uninstalled", $"'{item.ThemeName}' was removed from your device.");
        }
        catch (Exception ex)
        {
            NotificationHelper.Show("Uninstall failed", ex.Message);
        }
    }

    private async void DeleteFromCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ThemeMarketplaceItem item)
            return;

        var confirmDialog = new ContentDialog
        {
            Title = "Delete from GitHub",
            Content = $"Are you sure you want to delete '{item.ThemeName}' from GitHub? " +
                      "This permanently removes it from the catalog for everyone. " +
                      "It will no longer appear in Discover Themes.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            Background = DialogHelper.GetAcrylicBrush(),
            RequestedTheme = ThemeHelper.GetCurrentTheme(),
            XamlRoot = XamlRoot
        };

        confirmDialog.Resources["ContentDialogMaxWidth"] = DialogHelper.MaxWidth;
        var result = await confirmDialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        try
        {
            await publishService.DeleteThemeAsync(item.ThemeName);

            itemCache.Remove(GetSafeDirectoryName(item.ThemeName));

            NotificationHelper.Show("Theme deleted", $"'{item.ThemeName}' has been removed from GitHub.");

            // catalog was updated optimistically by the delete — just re-filter
            catalog = await marketplaceService.GetCatalogCachedAsync();
            ApplyFilter(SearchBox.Text.Trim());
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
        await LoadMyThemesAsync();
    }

    private static string GetSafeDirectoryName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(name.Trim().Select(character =>
            Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character));
        return string.IsNullOrWhiteSpace(safeName) ? "unnamed-theme" : safeName;
    }
}