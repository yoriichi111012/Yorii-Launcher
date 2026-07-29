using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Yorii_Launcher.Helpers;

namespace Yorii_Launcher
{
    public sealed partial class SettingsPage : Page
    {
        // to prevent settings handlers from firing while the page is loading
        private bool isInitializing = true;
        // set false when navigating away so delayed callbacks skip UI access
        private bool isActive = true;
        // reference to mainwindow
        public MainWindow MainApp => MainWindow.Instance!;

        public SettingsPage()
        {
            InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required; // caching homepage and settings apge for faster navigation... i think installed mods page and download mods page are cached too

            // load current states
            LoadSelectedRamAmount();
            LoadThemeComboBox();
            LoadBackgroundImageComboBox();
            LoadOverlaySettings();
            LoadWindowBehaviorComboBox();
            LoadShowConsoleSetting();
            LoadSavedFolder();
            LoadInstancesSetting();
            LoadServerListSetting();
            LoadWorldListSetting();
            LoadHomeReleaseNotesSetting();
            LoadUpdateVersion();
            ShowCachedUpdateIfAvailable();
            //LoadAccentColor();

            isInitializing = false;
        }

        //private async void LoadAccentColor()
        //{
        //    var color = accentColorPicker.Color.ToString();
        //    Debug.WriteLine($"[Settings] Current accent color: {color}");

        //    accentColor.Text = color;
        //}



        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            isActive = true;

            if (e.Parameter is string section &&
                section.Equals("memory", StringComparison.OrdinalIgnoreCase))
            {
                // wait a frame so the scrollview layout is ready before scrolling
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    if (!isActive) return;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!isActive) return;
                        memoryCard.StartBringIntoView();
                        ramAmount.Focus(FocusState.Programmatic);
                    });
                });
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            isActive = false;
        }

        private async void PickFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.IsEnabled = false;
                pickedFolderTextBlock.Text = "";

                var picker = new FolderPicker(button.XamlRoot.ContentIslandEnvironment.AppWindowId)
                {
                    CommitButtonText = "Change Folder",
                    SuggestedStartLocation = PickerLocationId.ComputerFolder,
                    ViewMode = PickerViewMode.List
                };
                // open picker 
                var folder = await picker.PickSingleFolderAsync();

                if (folder != null)
                {
                    // set path
                    SettingsManager.Current.MinecraftPath = folder.Path;
                    SettingsManager.SaveSettings();
                    pickedFolderTextBlock.Text = "Current Folder: " + folder.Path;
                }
                else
                {
                    // reload current path
                    pickedFolderTextBlock.Text = $"Current Folder: {SettingsManager.Current.MinecraftPath}";
                }

                button.IsEnabled = true;
            }
        }

        private void RamAmount_ValueChanged(object _, RangeBaseValueChangedEventArgs e)
        {
            if (isInitializing) return;

            // update ramdisplay text and save current value
            ramDisplay.Text = e.NewValue + " GB";
            SettingsManager.Current.RamAmount = e.NewValue; // save the settings
            SettingsManager.SaveSettings();
        }

        private void LoadSelectedRamAmount()
        {
            ramAmount.Value = SettingsManager.Current.RamAmount;
            ramDisplay.Text = ramAmount.Value + " GB";
        }

        private void LoadBackgroundImageComboBox()
        {
            // check if image path is not empty and custom is selected in the combobox
            var savedPath = SettingsManager.Current.BackgroundImagePath;

            if (!string.IsNullOrEmpty(savedPath))
            {
                foreach (ComboBoxItem item in backgroundImage.Items.OfType<ComboBoxItem>())
                {
                    if (item.Tag?.ToString() == "Custom")
                    {
                        backgroundImage.SelectedItem = item;
                        backgroundImageTextBlock.Text = "Current Image: " + savedPath;
                        return;
                    }
                }
            }
            backgroundImage.SelectedIndex = 0;
            backgroundImageTextBlock.Text = "Current Image: None";
        }

        private void LoadOverlaySettings()
        {
            overlayOpacitySlider.Value = SettingsManager.Current.OverlayOpacity;
            overlayBlurToggle.IsOn = SettingsManager.Current.OverlayBlurEnabled;
        }

        private void SaveVersionFilters()
        {
            var vm = MainApp.VersionVM;
            SettingsManager.Current.ShowSnapshots = vm.ShowSnapshots;
            SettingsManager.Current.ShowFabric = vm.ShowFabric;
            SettingsManager.Current.ShowOld = vm.ShowOld;
            SettingsManager.SaveSettings();
        }

        private void LoadSavedFolder()
        {
            var path = SettingsManager.Current.MinecraftPath;
            if (!string.IsNullOrEmpty(path))
                pickedFolderTextBlock.Text = "Current Folder: " + path;
        }

        private void LoadInstancesSetting()
        {
            instancesToggleSwitch.IsOn = SettingsManager.Current.InstancesEnabled;
        }

        private void LoadServerListSetting()
        {
            serverListToggle.IsOn = SettingsManager.Current.ServerListEnabled;
        }

        private void LoadWorldListSetting()
        {
            worldListToggle.IsOn = SettingsManager.Current.WorldListEnabled;
        }

        private void LoadHomeReleaseNotesSetting()
        {
            homeReleaseNotesToggle.IsOn = SettingsManager.Current.ShowReleaseNotesOnHome;
        }

        private void LoadShowConsoleSetting()
        {
            showConsoleToggle.IsOn = SettingsManager.Current.ShowConsole;
        }

        private void ShowConsoleToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isInitializing) return;
            SettingsManager.Current.ShowConsole = showConsoleToggle.IsOn;
            SettingsManager.SaveSettings();
        }

        private async void InstancesToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (isInitializing) return;

            SettingsManager.Current.InstancesEnabled = instancesToggleSwitch.IsOn;
            SettingsManager.SaveSettings();

            if (MainWindow.Instance != null) // since mainwindow is cached it wouldn't call refreshinstancecontextasync again so we call it here
            {
                MainWindow.Instance.ApplyInstancesNavigationVisibility();
                await MainWindow.Instance.RefreshInstanceContextAsync();
            }
        }

        private void ServerListToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isInitializing) return;
            SettingsManager.Current.ServerListEnabled = serverListToggle.IsOn;
            if (!serverListToggle.IsOn)
                SettingsManager.Current.SelectedServerAddress = "";
            SettingsManager.SaveSettings();
        }

        private void WorldListToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isInitializing) return;
            SettingsManager.Current.WorldListEnabled = worldListToggle.IsOn;
            if (!worldListToggle.IsOn)
                SettingsManager.Current.SelectedWorldId = "";
            SettingsManager.SaveSettings();
        }

        private void LoadExperimentalResourcePackSetting()
        {
            experimentalResourcePackToggle.IsOn = SettingsManager.Current.ExperimentalResourcePackAnyVersion;
        }

        private void HomeReleaseNotesToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isInitializing) return;
            SettingsManager.Current.ShowReleaseNotesOnHome = homeReleaseNotesToggle.IsOn;
            SettingsManager.SaveSettings();
        }

        private void ExperimentalResourcePackToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isInitializing) return;
            SettingsManager.Current.ExperimentalResourcePackAnyVersion = experimentalResourcePackToggle.IsOn;
            SettingsManager.SaveSettings();
        }

        private void LoadThemeComboBox()
        {
            var savedTheme = SettingsManager.Current.CurrentTheme;
            foreach (ComboBoxItem item in themeMode.Items.OfType<ComboBoxItem>())
            {
                if ((item.Content?.ToString() ?? "") == savedTheme)
                {
                    themeMode.SelectedItem = item;
                    return;
                }
            }
            themeMode.SelectedIndex = 2; // by default use system setting
        }

        private void ThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (themeMode.SelectedItem is ComboBoxItem item)
            {
                string theme = item.Content?.ToString() ?? "System";

                switch (theme)
                {
                    case "Light":
                        ThemeHelper.ApplyTheme(ElementTheme.Light);
                        break;
                    case "Dark":
                        ThemeHelper.ApplyTheme(ElementTheme.Dark);
                        break;
                    default:
                        ThemeHelper.ApplyTheme(ElementTheme.Default);
                        break;
                }
                SettingsManager.Current.CurrentTheme = theme;
                SettingsManager.SaveSettings();
            }
        }

        private void LoadWindowBehaviorComboBox()
        {
            var savedBehavior = SettingsManager.Current.WindowBehavior;

            foreach (ComboBoxItem item in windowBehavior.Items.OfType<ComboBoxItem>())
            {
                if ((item.Tag?.ToString() ?? "") == savedBehavior)
                {
                    windowBehavior.SelectedItem = item;
                    return;
                }
            }
            windowBehavior.SelectedIndex = 0;
        }

        private void WindowBehavior_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (windowBehavior.SelectedItem is ComboBoxItem item)
            {
                SettingsManager.Current.WindowBehavior = item.Tag?.ToString() ?? "None";
                SettingsManager.SaveSettings();
            }
        }

        private async void backgroundImage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitializing) return;

            // ensure selected item is a valid background option
            if (backgroundImage.SelectedItem is not ComboBoxItem item)
                return;

            if (item.Tag?.ToString() == "None")
            {
                SettingsManager.Current.BackgroundImagePath = "";
                SettingsManager.SaveSettings();
                backgroundImageTextBlock.Text = "Current Image: None";
                MainWindow.Instance?.ApplyBackgroundSettings();
                return;
            }

            if (item.Tag?.ToString() == "Custom")
            {
                var picker = new FileOpenPicker(backgroundImage.XamlRoot.ContentIslandEnvironment.AppWindowId)
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    ViewMode = PickerViewMode.Thumbnail
                };

                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".webp");
                // open picker
                var file = await picker.PickSingleFileAsync();

                if (file != null)
                {
                    SettingsManager.Current.BackgroundImagePath = file.Path;
                    SettingsManager.SaveSettings();
                    backgroundImageTextBlock.Text = "Current Image: " + file.Path;
                    MainWindow.Instance?.ApplyBackgroundSettings();
                }
                else
                {
                    if (string.IsNullOrEmpty(SettingsManager.Current.BackgroundImagePath))
                    {
                        isInitializing = true;
                        backgroundImage.SelectedIndex = 0;
                        backgroundImageTextBlock.Text = "Current Image: None";
                        // enable all handlers after initialization is done
                        isInitializing = false;
                    }
                }
            }
        }

        private void OverlayOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (isInitializing) return;
            SettingsManager.Current.OverlayOpacity = e.NewValue;
            SettingsManager.SaveSettings();
            MainWindow.Instance?.ApplyBackgroundSettings();
        }

        private void OverlayBlurToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (isInitializing) return;
            SettingsManager.Current.OverlayBlurEnabled = overlayBlurToggle.IsOn;
            SettingsManager.SaveSettings();
            MainWindow.Instance?.ApplyBackgroundSettings();
        }

        private UpdateService.UpdateInfo? pendingUpdate;

        private void LoadUpdateVersion()
        {
            var current = UpdateService.GetCurrentVersion();
            updateStatusText.Text = $"v{current.Major}.{current.Minor}.{current.Build}";
        }

        // show cached update result from startup check if available
        private void ShowCachedUpdateIfAvailable()
        {
            var cached = UpdateService.LastCheckedUpdate;
            if (cached == null) return;

            pendingUpdate = cached;
            updateVersionText.Text = $"v{cached.Version.Major}.{cached.Version.Minor}.{cached.Version.Build}";
            updateStatusText.Text = "Update available";
            ShowUpdateAvailableUI();
        }

        private void ShowUpdateAvailableUI()
        {
            checkUpdateButton.Visibility = Visibility.Collapsed;
            updateAvailableCard.Visibility = Visibility.Visible;
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.IsEnabled = false;
                button.Content = "Checking...";

                try
                {
                    var updateInfo = await UpdateService.CheckForUpdateAsync();
                    pendingUpdate = updateInfo;

                    if (updateInfo != null)
                    {
                        updateVersionText.Text = $"v{updateInfo.Version.Major}.{updateInfo.Version.Minor}.{updateInfo.Version.Build}";
                        updateStatusText.Text = "Update available";
                        ShowUpdateAvailableUI();
                    }
                    else
                    {
                        updateAvailableCard.Visibility = Visibility.Collapsed;
                        checkUpdateButton.Visibility = Visibility.Visible;
                        updateStatusText.Text = "Up to date";
                    }
                }
                catch (Exception ex)
                {
                    NotificationHelper.Show("Update check failed", $"An error occurred while checking for updates. Please try again later. Exception : {ex.Message}");
                    updateStatusText.Text = "Check failed";
                }
                finally
                {
                    button.Content = "Check";
                    button.IsEnabled = true;
                }
            }
        }

        // download msix and open it with system handler
        private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (pendingUpdate?.DownloadUrl == null) return;

            updateAvailableCard.Visibility = Visibility.Collapsed;
            updateProgressCard.Visibility = Visibility.Visible;
            installUpdateButton.IsEnabled = false;

            try
            {
                var progress = new Progress<double>(percent =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        updateProgressText.Text = $"{percent:F0}%"; // round to whole number
                    });
                });

                var msixPath = await UpdateService.DownloadUpdateAsync(pendingUpdate, progress);

                if (msixPath == null)
                {
                    updateProgressCard.Visibility = Visibility.Collapsed;
                    updateStatusText.Text = "Download failed";
                    checkUpdateButton.Visibility = Visibility.Visible;
                    return;
                }

                updateProgressCard.Visibility = Visibility.Collapsed;
                updateStatusText.Text = "Download complete. Install the update from the window that opened.";
                UpdateService.LaunchMsix(msixPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] Download failed: {ex.Message}");
                updateProgressCard.Visibility = Visibility.Collapsed;
                updateStatusText.Text = "Download failed";
                checkUpdateButton.Visibility = Visibility.Visible;
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker(((Button)sender).XamlRoot.ContentIslandEnvironment.AppWindowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "yorii-settings",
                CommitButtonText = "Export"
            };
            picker.FileTypeChoices.Add("JSON Files", [".json"]);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                SettingsManager.ExportSettings(file.Path);
                NotificationHelper.Show("Settings exported", "Your settings have been saved.");
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker(((Button)sender).XamlRoot.ContentIslandEnvironment.AppWindowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".json"); // pick json file

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    SettingsManager.ImportSettings(file.Path);
                    NotificationHelper.Show("Settings imported", "Your settings have been restored. Restarting the launcher is recommended.");
                    isInitializing = true;
                    LoadSelectedRamAmount();
                    LoadThemeComboBox();
                    LoadBackgroundImageComboBox();
                    LoadOverlaySettings();
                    LoadWindowBehaviorComboBox();
                    LoadShowConsoleSetting();
                    LoadSavedFolder();
                    LoadInstancesSetting();
                    LoadServerListSetting();
                    LoadWorldListSetting();
                    LoadHomeReleaseNotesSetting();
                    LoadExperimentalResourcePackSetting();
                    isInitializing = false;
                    MainWindow.Instance?.ApplyBackgroundSettings();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Settings] Import failed: {ex.Message}");
                    NotificationHelper.Show("Import failed", "The settings file could not be read.");
                }
            }
        }

        //private void accentColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
        //{
        //    var color = accentColorPicker.Color.ToString();
        //    Debug.WriteLine($"[Settings] Current accent color: {color}");

        //    accentColor.Text = color;
        //}
    }
}