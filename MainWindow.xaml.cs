using CmlLib.Core;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionLoader;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;
using Yorii_Launcher.ViewModels;

namespace Yorii_Launcher
{
    public sealed partial class MainWindow : Window
    {
        // set global minecraftPath to better code structure
        private string minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();
        // make MainWindow accesible from other pages
        public static MainWindow? Instance { get; set; }
        public VersionViewModel VersionVM { get; } = new VersionViewModel();
        private readonly ObservableCollection<AccountComboItem> accountItems = [];
        private double downloadProgressValue; 
        private ContentDialog? managePlayersDialog;
        // set variables for background image
        private string currentImagePath = "";

        public MainWindow()
        {
            InitializeComponent();

            VersionVM.FilteredVersions.CollectionChanged += (_, __) =>
            {
                versionComboBox.ItemsSource = null;
                versionComboBox.ItemsSource = VersionVM.FilteredVersions;
            };

            // set window size icon and title bar
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1176, 661));
            SetWindowIcon();
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(titleBar);

            // call functions to load versions and accounts
            ApplyBackgroundSettings();
            LoadAccounts();
            LoadVersionFilters();
            _ = LoadVersionsAsync();

            Instance = this;
            rootGrid.ActualThemeChanged += (_, __) => ApplyBackgroundSettings();
            mainFrame.Navigate(typeof(HomePage));
        }

        private void LoadAccounts()
        {
            // prevent duplicate accounts by clearing before loading
            accountItems.Clear();

            foreach (var account in AccountManager.LoadAccounts())
            {
                accountItems.Add(AccountComboItem.ForAccount(account));
            }

            // add the players accounts and management options
            accountItems.Add(AccountComboItem.ManagePlayers);
            accountItems.Add(AccountComboItem.AddNew);
            accountComboBox.ItemsSource = accountItems;

            var selectedAccount = AccountManager.GetSelectedAccount();

            // fallback
            if (selectedAccount != null)
            {
                accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => x.Account?.Id == selectedAccount.Id);
            }
        }

        private void LoadVersionFilters()
        {
            // load filter settings
            VersionVM.ShowSnapshots = SettingsManager.Current.ShowSnapshots;
            VersionVM.ShowFabric = SettingsManager.Current.ShowFabric;
            VersionVM.ShowOld = SettingsManager.Current.ShowOld;
        }

        public void ApplyBackgroundSettings()
        {
            // get current background image path
            string imagePath = SettingsManager.Current.BackgroundImagePath ?? "";
            // check if current background image path is null and whether the file exists
            bool hasImage = !string.IsNullOrEmpty(imagePath) && File.Exists(imagePath);

            if (imagePath != currentImagePath)
            {
                currentImagePath = imagePath;

                if (hasImage)
                {
                    try
                    {
                        // apply background image (using a filestream to prevent locking the file so the user can change or delete it without restarting the launcher)
                        using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var bmp = new BitmapImage();
                        bmp.SetSource(fs.AsRandomAccessStream());
                        backgroundImage.Source = bmp;
                    }
                    catch
                    {
                        backgroundImage.Source = null;
                        hasImage = false;
                    }
                }
                else
                {
                    backgroundImage.Source = null;
                }
            }

            backgroundImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
            overlayGrid.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;

            if (!hasImage)
            {
                overlayGrid.Opacity = 0;
                rootGrid.Background = null;
                return;
            }

            double opacity = SettingsManager.Current.OverlayOpacity;
            bool blurEnabled = SettingsManager.Current.OverlayBlurEnabled;

            // overlay is white in light mode so checking here for that
            bool isLight = ThemeHelper.GetCurrentTheme() == ElementTheme.Light;
            byte alpha = (byte)(opacity * 255);

            var tint = isLight
                ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                : Windows.UI.Color.FromArgb(255, 0, 0, 0);

            var fallback = isLight
                ? Windows.UI.Color.FromArgb(alpha, 255, 255, 255)
                : Windows.UI.Color.FromArgb(alpha, 0, 0, 0);

            if (blurEnabled)
            {
                overlayGrid.Opacity = 1;
                overlayGrid.Background = new AcrylicBrush
                {
                    TintOpacity = opacity,
                    TintColor = tint,
                    FallbackColor = fallback
                };
            }
            else
            {
                overlayGrid.Opacity = 1;
                overlayGrid.Background = new SolidColorBrush(fallback);
            }
        }

        private async Task LoadVersionsAsync()
        {
            try
            {
                VersionVM.AllVersions.Clear();
                VersionVM.FilteredVersions.Clear();
                versionComboBox.SelectedItem = null;

                var instancesEnabled = SettingsManager.Current.InstancesEnabled;
                var selectedInstance = InstanceManager.GetSelectedInstance();
                // check if instances are enabled but no instance is selected, if yes then empty version list combobox
                if (instancesEnabled && selectedInstance == null)
                {
                    VersionVM.ApplyFilters();
                    versionComboBox.ItemsSource = VersionVM.FilteredVersions;
                    return;
                }
                // prevents crash when minecraftpath is null
                if (string.IsNullOrEmpty(minecraftPath))
                    return;

                Directory.CreateDirectory(minecraftPath);
                Directory.CreateDirectory(Path.Combine(minecraftPath, "versions"));

                var path = new MinecraftPath(minecraftPath);
                var launcher = new MinecraftLauncher(path);

                // load local versions from versions folder
                string versionsPath = Path.Combine(minecraftPath, "versions");
                foreach (var dir in Directory.GetDirectories(versionsPath))
                {
                    string versionName = Path.GetFileName(dir);
                    string jsonPath = Path.Combine(dir, versionName + ".json");

                    if (File.Exists(jsonPath))
                    {
                        VersionVM.AllVersions.Add(new VersionItem
                        {
                            Name = versionName,
                            IsFabric = versionName.StartsWith("Fabric ", StringComparison.OrdinalIgnoreCase) || versionName.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase),
                            IsSnapshot = versionName.Contains("snapshot"),
                            IsOld = Version.TryParse(versionName, out var v) && v < new Version(1, 16)
                        });
                    }
                }

                try
                {
                    // load fabric versions from server
                    var fabricInstaller = new FabricInstaller(HttpService.Client);
                    var fabricVersions = await fabricInstaller.GetSupportedVersionNames();

                    foreach (var v in fabricVersions)
                    {
                        string fabricName = $"Fabric {v}";

                        if (!VersionVM.AllVersions.Any(x => x.Name == fabricName))
                        {
                            VersionVM.AllVersions.Add(new VersionItem
                            {
                                Name = fabricName,
                                IsFabric = true,
                                IsSnapshot = false,
                                IsOld = false
                            });
                        }
                    }
                }
                catch
                {
                    Debug.WriteLine("Failed to load fabric versions from server");
                }

                try
                {
                    // load vanilla versions from server
                    var vanillaVersions = await launcher.GetAllVersionsAsync();
                    foreach (var v in vanillaVersions)
                    {
                        if (v.Type != "release" && v.Type != "snapshot")
                            continue;

                        string vanillaName = v.Name;
                        bool canParse = Version.TryParse(v.Name, out var version);

                        if (!VersionVM.AllVersions.Any(x => x.Name == vanillaName))
                        {
                            VersionVM.AllVersions.Add(new VersionItem
                            {
                                Name = vanillaName,
                                IsSnapshot = v.Type == "snapshot",
                                IsFabric = false,
                                IsOld = canParse && version < new Version(1, 16)
                            });
                        }
                    }
                }
                catch
                {
                    Debug.WriteLine("Failed to load vanilla versions from server");
                }

                // apply filters and fill version list
                VersionVM.ApplyFilters();
                versionComboBox.ItemsSource = VersionVM.FilteredVersions;
                LoadSavedVersion();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        public void LoadSavedVersion()
        {
            var savedVersion = SettingsManager.Current.InstancesEnabled // if entances are enabled then current instance's version else null
                ? InstanceManager.GetSelectedInstanceVersion()
                : null;

            if (string.IsNullOrWhiteSpace(savedVersion) && !string.IsNullOrEmpty(SettingsManager.Current.LastSavedVersion)) // load last saved version when instance disabled
                savedVersion = SettingsManager.Current.LastSavedVersion;

            if (!string.IsNullOrWhiteSpace(savedVersion))
            {
                // check if saved version is in filtered versions list or not if yes then select it
                foreach (var item in VersionVM.FilteredVersions)
                {
                    if (item == savedVersion)
                    {
                        versionComboBox.SelectedItem = item;
                        return;
                    }
                }
            }

            if (VersionVM.FilteredVersions.Count > 0) // otherwise just select the topmost in the list
                versionComboBox.SelectedIndex = 0;
        }

        public async Task RefreshInstanceContextAsync()
        {
            // reload current selected version for instances
            await LoadVersionsAsync();
            MemoryOptimizer.ReduceMemory();
        }

        private static string EnsureAuthlibInjector()
        {
            // ensure authlib-injector.jar file exists in game folder if not then copy it from the launcher directory
            // ProgramData is used cause no spaces are there in address because trying to load from an address with spaces doesn't work
            string launcherDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "YoriiLauncher");
            Directory.CreateDirectory(launcherDir);
            // current authlib version is 1.2.7
            string injectorPath = Path.Combine(launcherDir, "authlib-injector.jar"); // in launcher directory
            string jarPath = Path.Combine(AppContext.BaseDirectory, "authlib-injector.jar"); // in programdata

            if (!File.Exists(jarPath))
                throw new FileNotFoundException("authlib-injector.jar missing in launcher directory", jarPath);

            // only copy if the file size is different
            if (File.Exists(injectorPath) && new FileInfo(jarPath).Length == new FileInfo(injectorPath).Length)
                return injectorPath;

            File.Copy(jarPath, injectorPath, true);
            return injectorPath;
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            // prevent double click from launching two processes
            playButton.IsEnabled = false;

            try
            {
                var account = GetSelectedPlayerAccount();

                if (account == null || string.IsNullOrWhiteSpace(account.Username))
                {
                    NotificationHelper.Show("No player selected", "Choose or add a player before launching.");
                    playButton.IsEnabled = true;
                    return;
                }

                string username = account.Username;
                string password = account.Password ?? "";

                bool hasInternet = await NetworkHelper.InternetAvailable();

                if (string.IsNullOrEmpty(minecraftPath))
                    minecraftPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

                var path = new MinecraftPath(minecraftPath);
                MinecraftLauncher launcher;

                // check for internet connectivity before launching to decide whether to attempt online login and online version loading or not
                if (hasInternet)
                    launcher = new MinecraftLauncher(path);
                else
                {
                    // only loads local versions and does not tries loading from the internet
                    var parameters = MinecraftLauncherParameters.CreateDefault(path);
                    parameters.VersionLoader = new LocalJsonVersionLoader(path);
                    launcher = new MinecraftLauncher(parameters);
                }

                // setting states for the progress bar
                downloadProgressBar.Opacity = 1;
                downloadProgressBar.Value = 0;
                downloadProgressBar.IsIndeterminate = true;
                downloadProgressValue = 0;
                
                // update the progress
                launcher.FileProgressChanged += (s, args) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (args.TotalTasks > 0)
                            SetDownloadProgress((double)args.ProgressedTasks / args.TotalTasks * 100);
                    });
                };

                launcher.ByteProgressChanged += (s, args) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (args.TotalBytes > 0)
                            SetDownloadProgress((double)args.ProgressedBytes / args.TotalBytes * 100);
                    });
                };

                string? selectedVersion = versionComboBox.SelectedItem?.ToString();

                if (string.IsNullOrWhiteSpace(selectedVersion))
                {
                    NotificationHelper.Show("No version selected", "Select or install a Minecraft version before launching.");
                    playButton.IsEnabled = true;
                    return;
                }

                var instancesEnabled = SettingsManager.Current.InstancesEnabled;
                var selectedInstance = InstanceManager.GetSelectedInstance();

                if (instancesEnabled && selectedInstance == null)
                {
                    NotificationHelper.Show("No instance selected", "Create or select an instance on the Home page first.");

                    if (mainFrame.CurrentSourcePageType != typeof(HomePage))
                        mainFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());

                    playButton.IsEnabled = true;
                    return;
                }

                bool isFabric = selectedVersion.StartsWith("Fabric ");
                string baseVersionFabric = isFabric
                    ? selectedVersion["Fabric ".Length..].Trim()
                    : selectedVersion;

                string versionToLaunch;

                playButton.IsEnabled = false;
                playButton.Content = "Downloading...";

                // check if current version is fabric
                if (isFabric)
                {
                    if (hasInternet)
                    {
                        var fabricInstaller = new FabricInstaller(HttpService.Client);
                        versionToLaunch = await fabricInstaller.Install(baseVersionFabric, path);
                        await launcher.InstallAsync(versionToLaunch);
                    }
                    else
                    {
                        Debug.WriteLine("Offline: skipping Fabric install");
                        versionToLaunch = $"Fabric {baseVersionFabric}";
                    }
                }
                else
                {
                    if (hasInternet)
                        await launcher.InstallAsync(selectedVersion);
                    else
                        Debug.WriteLine("Offline: skipping vanilla install");

                    versionToLaunch = selectedVersion;
                }

                playButton.Content = "Launching...";
                downloadProgressBar.IsIndeterminate = false;
                downloadProgressBar.Value = 100;
                downloadProgressValue = 100;

                // set default ram amount to 2 GB
                double ramGb = SettingsManager.Current.RamAmount;

                // convert GB to MB for the launch arguments
                int ramMb = (int)(ramGb * 1024);

                LoginHelper.LoginResult loginResult;
                bool isMojangAccount = account.AccountType == PlayerAccountType.Mojang && !string.IsNullOrEmpty(account.MojangIdentifier);

                // microsoft account
                if (isMojangAccount)
                {
                    var session = await LoginHelper.LoginWithMojangSilently(account.MojangIdentifier!);
                    loginResult = new LoginHelper.LoginResult
                    {
                        Session = session,
                        IsOffline = false
                    };
                }
                else
                {
                    // ely.by or offline
                    loginResult = await LoginHelper.LoginOrUseCachedSession(username, password);
                }

                List<MArgument> jvmArgs = [];

                if (isMojangAccount)
                {
                    // we don't want authlib-injector in official logins because we're using official mojang auth
                    Debug.WriteLine("Mojang account: launching without authlib-injector");
                }
                else if (!loginResult.IsOffline)
                {
                    // for ely.by accounts and cached sessions
                    string injectorPath = EnsureAuthlibInjector();
                    Debug.WriteLine("we are online!!");
                    jvmArgs.Add(new MArgument($"-javaagent:{injectorPath}=ely.by"));
                }
                else
                {
                    // cached session but still has internet otherwise no authlib-injector and completely offline mode
                    if (hasInternet)
                    {
                        string injectorPath = EnsureAuthlibInjector();
                        jvmArgs.Add(new MArgument($"-javaagent:{injectorPath}=ely.by"));
                    }
                    Debug.WriteLine("using offline cached mode");
                }

                // W server list
                var selectedServerAddress = ServerManager.GetSelectedServerAddress();

                // begin building the launch options
                var launchOption = new MLaunchOption
                {
                    MaximumRamMb = ramMb,
                    Session = loginResult.Session,
                    ExtraJvmArguments = jvmArgs.ToArray()
                };

                if (!string.IsNullOrWhiteSpace(selectedServerAddress))
                {
                    var serverHost = selectedServerAddress;
                    var serverPort = 25565;

                    // split ip and port from servers.dat, port only included if non-default
                    var colonIdx = selectedServerAddress.LastIndexOf(':');
                    if (colonIdx > 0 && 
                        int.TryParse(selectedServerAddress[(colonIdx + 1)..], out var parsedPort) && 
                        parsedPort > 0 && 
                        parsedPort <= 65535)
                    {
                        serverHost = selectedServerAddress[..colonIdx];
                        serverPort = parsedPort;
                    }

                    // set autojoin target server
                    launchOption.ServerIp = serverHost;
                    launchOption.ServerPort = serverPort;
                }

                var process = await launcher.BuildProcessAsync(versionToLaunch, launchOption);

                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;

                // empty working set before starting the game since the launcher is no longer needed
                MemoryOptimizer.ReduceMemory();
                process.Start();

                // set up console
                bool showConsole = SettingsManager.Current.ShowConsole;

                Console? console = null;
                if (showConsole)
                {
                    console = new Console();
                    console.Clear();
                    console.AppendLine($"[{DateTime.Now:HH:mm:ss}] Launching {versionToLaunch}...");
                    console.AppendLine($"[{DateTime.Now:HH:mm:ss}] ---");
                    console.Activate();
                }

                // set up launcher window behavior
                string behavior = SettingsManager.Current.WindowBehavior;

                switch (behavior)
                {
                    case "Hide":
                        AppWindow.Hide();
                        break;
                    case "Close":
                        this.Close();
                        break;
                }

                // save the latest played instance so what it appears first in the instances section
                if (selectedInstance != null)
                    InstanceManager.MarkPlayed(selectedInstance.Id);

                // start the game
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using StreamReader stdout = process.StandardOutput;
                        using StreamReader stderr = process.StandardError;

                        var stdoutTask = Task.Run(async () =>
                        {
                            string? line;
                            while ((line = await stdout.ReadLineAsync()) != null)
                            {
                                Debug.WriteLine($"{line}");
                                console?.AppendLine(line);
                            }
                        });

                        var stderrTask = Task.Run(async () =>
                        {
                            string? line;
                            while ((line = await stderr.ReadLineAsync()) != null)
                            {
                                Debug.WriteLine($"{line}");
                                console?.AppendLine($"{line}");
                            }
                        });

                        // wait for both output tasks to complete which will happen when the game exits
                        await Task.WhenAll(stdoutTask, stderrTask);
                    }
                    catch (IOException ex) when (ex.HResult == unchecked((int)0x800703E3) || ex.Message.Contains("aborted", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine("Game Died.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Launcher Error - {ex} - {ex.Message}");
                    }
                    finally
                    {
                        await process.WaitForExitAsync();

                        DispatcherQueue.TryEnqueue(() =>
                        {
                            playButton.Content = "Play";
                            playButton.IsEnabled = true;
                            // show window if hidden
                            AppWindow.Show();
                            downloadProgressBar.Opacity = 0;
                            downloadProgressBar.IsIndeterminate = false;
                            MemoryOptimizer.ReduceMemory();
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Launch failed: {ex}");
                playButton.Content = "Play";
                playButton.IsEnabled = true;
                downloadProgressBar.Opacity = 0;
                downloadProgressBar.IsIndeterminate = false;

                // check if it was network issue
                var isNetwork = ex is System.Net.Http.HttpRequestException
                    or System.Net.Sockets.SocketException
                    or TaskCanceledException;

                ShowNotification("Launch failed",isNetwork
                        ? "Could not connect to the internet. Check your connection and try again."
                        : $"An error occurred: {ex.Message}");
            }
        }

        private void AccountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ensure selected item is a valid account
            if (accountComboBox.SelectedItem is not AccountComboItem item)
                return;

            // manage players is selected
            if (item.IsManagePlayers)
            {
                accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => !x.IsAddNew && !x.IsManagePlayers);
                _ = ShowManagePlayersDialogAsync();
                return;
            }

            // add player is selected
            if (item.IsAddNew)
            {
                accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => !x.IsAddNew && !x.IsManagePlayers);
                _ = ShowAddPlayerDialogAsync();
                return;
            }

            // player account is selected
            if (item.Account != null)
                AccountManager.SetSelectedAccount(item.Account.Id);
        }

        private PlayerAccount? GetSelectedPlayerAccount()
        {
            // get the current player, fall back to saved account if add player or manage players is selected
            if (accountComboBox.SelectedItem is AccountComboItem { IsAddNew: false } item)
                return item.Account;

            return AccountManager.GetSelectedAccount();
        }

        private async Task ShowAddPlayerDialogAsync()
        {
            // create the elements in the dialog
            var usernameBox = new TextBox
            {
                Header = "Username",
                PlaceholderText = "Player name"
            };

            var passwordBox = new PasswordBox
            {
                Header = "Password",
                PlaceholderText = "Leave empty for offline"
            };

            var accountTypeBox = new ComboBox
            {
                Header = "Account type",
                SelectedIndex = 0
            };

            accountTypeBox.Items.Add(new ComboBoxItem { Content = "Ely.by", Tag = PlayerAccountType.ElyBy });
            accountTypeBox.Items.Add(new ComboBoxItem { Content = "Mojang (Microsoft)", Tag = PlayerAccountType.Mojang });

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(accountTypeBox);
            panel.Children.Add(usernameBox);
            panel.Children.Add(passwordBox);

            // changed username and password visibility based on whether mojang account is selected or not
            accountTypeBox.SelectionChanged += (_, _) =>
            {
                bool isMojang = accountTypeBox.SelectedItem is ComboBoxItem item && item.Tag is PlayerAccountType.Mojang;
                usernameBox.Visibility = isMojang ? Visibility.Collapsed : Visibility.Visible;
                passwordBox.Visibility = isMojang ? Visibility.Collapsed : Visibility.Visible;
            };

            // get theme to apply to the dialog
            ElementTheme theme = ThemeHelper.GetCurrentTheme();

            // create the dialog
            var dialog = new ContentDialog
            {
                Title = "Add player",
                Content = panel,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = rootGrid.XamlRoot,
                RequestedTheme = theme
            };

            // show the dialog and wait for the result
            var result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
                return;

            // ensure selected item is a valid account type
            if (accountTypeBox.SelectedItem is not ComboBoxItem selectedTypeItem ||
                selectedTypeItem.Tag is not PlayerAccountType accountType)
            {
                accountType = PlayerAccountType.ElyBy;
            }

            if (accountType == PlayerAccountType.Mojang)
            {
                try
                {
                    playButton.IsEnabled = false;
                    playButton.Content = "Signing in...";

                    // launch window for microsoft login
                    var (session, identifier) = await LoginHelper.LoginWithMojangInteractive();

                    var account = new PlayerAccount
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Username = session.Username,
                        Password = null,
                        AccountType = PlayerAccountType.Mojang,
                        MojangIdentifier = identifier
                    };

                    AccountManager.SaveAccount(account);
                    LoadAccounts();
                    accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => x.Account?.Id == account.Id);

                    ShowNotification("Account added", $"Signed in as {session.Username}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Mojang login failed: {ex}");
                    ShowNotification("Login failed", ex.Message);
                }
                finally
                {
                    playButton.Content = "Play";
                    playButton.IsEnabled = true;
                }
                return;
            }

            var username = usernameBox.Text.Trim();
            // check if username is empty
            if (string.IsNullOrWhiteSpace(username))
            {
                ShowNotification("Username is empty", "Enter a player name before adding the account.");
                return;
            }

            var password = passwordBox.Password;

            bool hasInternet = await NetworkHelper.InternetAvailable();

            // when internet is available, password not empty and account ely.by
            if (!string.IsNullOrWhiteSpace(password) && hasInternet && accountType == PlayerAccountType.ElyBy)
            {
                try
                {
                    await LoginHelper.LoginWithElyBy(username, password);
                }
                catch (Exception ex) when (ex.Message == "INVALID_CREDENTIALS")
                {
                    ShowNotification("Login failed", "Please verify your Ely.by account credentials.");
                    return;
                }
                catch
                {
                    ShowNotification("Login failed", "Something went wrong.");
                    return;
                }
            }

            var elyAccount = new PlayerAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                Username = username,
                Password = string.IsNullOrWhiteSpace(password) ? null : password,
                AccountType = accountType
            };

            AccountManager.SaveAccount(elyAccount);
            LoadAccounts();
            accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => x.Account?.Id == elyAccount.Id);
        }

        private async Task ShowManagePlayersDialogAsync()
        {
            // load accounts and theme
            var accounts = AccountManager.LoadAccounts();
            var theme = ThemeHelper.GetCurrentTheme();

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 420
            };

            // set main stackpanel
            var itemsPanel = new StackPanel { Spacing = 0 };

            foreach (var account in accounts)
            {
                var nameBlock = new TextBlock
                {
                    Text = account.Username,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var typeBlock = new TextBlock
                {
                    Text = account.IsOffline ? "Offline" : PlayerAccount.GetAccountTypeLabel(account.AccountType),
                    FontSize = 12,
                    Opacity = 0.7,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var textStack = new StackPanel
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center
                };
                textStack.Children.Add(nameBlock);
                textStack.Children.Add(typeBlock);

                var editButton = new Button
                {
                    Width = 32,
                    Height = 32,
                    Padding = new Thickness(0),
                    Tag = account,
                };
                ToolTipService.SetToolTip(editButton, "Edit player");
                editButton.Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 };
                editButton.Click += ManagePlayer_Edit_Click;

                var deleteButton = new Button
                {
                    Width = 32,
                    Height = 32,
                    Padding = new Thickness(0),
                    Tag = account
                };
                ToolTipService.SetToolTip(deleteButton, "Delete player");
                deleteButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 };
                deleteButton.Click += ManagePlayer_Delete_Click;

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                buttonsPanel.Children.Add(editButton);
                buttonsPanel.Children.Add(deleteButton);

                var rowGrid = new Grid
                {
                    Padding = new Thickness(12, 10, 12, 10)
                };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(textStack, 0);
                Grid.SetColumn(buttonsPanel, 1);
                rowGrid.Children.Add(textStack);
                rowGrid.Children.Add(buttonsPanel);

                var rowBorder = new Border
                {
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 0, 4),
                    Child = rowGrid
                };

                itemsPanel.Children.Add(rowBorder);
            }

            // if no accounts
            if (accounts.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "No players added yet.",
                    FontSize = 12,
                    Opacity = 0.7,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 24)
                };
                itemsPanel.Children.Add(emptyText);
            }

            scrollViewer.Content = itemsPanel;

            // create dialog
            managePlayersDialog = new ContentDialog
            {
                Title = "Manage players",
                Content = scrollViewer,
                CloseButtonText = "Close",
                XamlRoot = rootGrid.XamlRoot,
                RequestedTheme = theme
            };

            // show dialog
            await managePlayersDialog.ShowAsync();

            managePlayersDialog = null;
            LoadAccounts();
        }

        private async void ManagePlayer_Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PlayerAccount account)
            {
                var dialog = managePlayersDialog;
                managePlayersDialog = null;
                dialog?.Hide();

                // show edit dialog for the selected account
                await ShowEditPlayerDialogAsync(account);
            }
        }

        private async void ManagePlayer_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PlayerAccount account)
            {
                var manageDialog = managePlayersDialog;
                managePlayersDialog = null;
                manageDialog?.Hide();

                // confirm deletion
                var confirmDialog = new ContentDialog
                {
                    Title = "Delete player",
                    Content = $"Delete {account.Username}?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = rootGrid.XamlRoot,
                    RequestedTheme = ThemeHelper.GetCurrentTheme()
                };

                var result = await confirmDialog.ShowAsync();

                if (result != ContentDialogResult.Primary)
                {
                    await ShowManagePlayersDialogAsync();
                    return;
                }

                // delete the account
                AccountManager.DeleteAccount(account.Id);

                if (!string.IsNullOrEmpty(account.MojangIdentifier))
                    _ = LoginHelper.RemoveMojangAccount(account.MojangIdentifier);

                // refresh
                LoadAccounts();

                await ShowManagePlayersDialogAsync();
            }
        }

        private async Task ShowEditPlayerDialogAsync(PlayerAccount account)
        {
            bool isMojang = account.AccountType == PlayerAccountType.Mojang; /// check if mojang account

            var usernameBox = new TextBox
            {
                Header = "Username",
                Text = account.Username,
                IsReadOnly = isMojang
            };

            var passwordBox = new PasswordBox
            {
                Header = "Password",
                PlaceholderText = "Leave empty for offline",
                Visibility = isMojang ? Visibility.Collapsed : Visibility.Visible
            };

            if (!string.IsNullOrWhiteSpace(account.Password))
                passwordBox.Password = account.Password;

            var accountTypeBox = new ComboBox
            {
                Header = "Account type"
            };

            accountTypeBox.Items.Add(new ComboBoxItem { Content = "Ely.by", Tag = PlayerAccountType.ElyBy });
            accountTypeBox.Items.Add(new ComboBoxItem { Content = "Mojang (Microsoft)", Tag = PlayerAccountType.Mojang });

            for (int i = 0; i < accountTypeBox.Items.Count; i++)
            {
                if (accountTypeBox.Items[i] is ComboBoxItem item && item.Tag is PlayerAccountType type && type == account.AccountType)
                {
                    accountTypeBox.SelectedIndex = i;
                    break;
                }
            }

            if (accountTypeBox.SelectedIndex < 0)
                accountTypeBox.SelectedIndex = 0;

            // will implement changing mojang username is next major release, for now only reauth
            accountTypeBox.SelectionChanged += (_, _) =>
            {
                bool nowMojang = accountTypeBox.SelectedItem is ComboBoxItem selItem && selItem.Tag is PlayerAccountType.Mojang;
                usernameBox.IsReadOnly = nowMojang;
                passwordBox.Visibility = nowMojang ? Visibility.Collapsed : Visibility.Visible;
            };

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(accountTypeBox);
            panel.Children.Add(usernameBox);
            panel.Children.Add(passwordBox);

            // if mojang account
            if (isMojang)
            {
                var infoText = new TextBlock
                {
                    Text = "Microsoft accounts are authenticated via OAuth. Click Save to re-authenticate.",
                    FontSize = 12,
                    Opacity = 0.6,
                    TextWrapping = TextWrapping.Wrap
                };
                panel.Children.Add(infoText);
            }

            // create dialog
            var dialog = new ContentDialog
            {
                Title = "Edit player",
                Content = panel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = rootGrid.XamlRoot,
                RequestedTheme = ThemeHelper.GetCurrentTheme()
            };

            // show dialog
            var dialogResult = await dialog.ShowAsync();

            if (dialogResult != ContentDialogResult.Primary)
                return;

            if (accountTypeBox.SelectedItem is ComboBoxItem selectedTypeItem &&
                selectedTypeItem.Tag is PlayerAccountType newAccountType)
            {
                if (newAccountType == PlayerAccountType.Mojang)
                {
                    if (account.AccountType != PlayerAccountType.Mojang)
                    {
                        try
                        {
                            playButton.IsEnabled = false;
                            playButton.Content = "Signing in...";

                            var (session, identifier) = await LoginHelper.LoginWithMojangInteractive();

                            account.Username = session.Username;
                            account.Password = null;
                            account.AccountType = PlayerAccountType.Mojang;
                            account.MojangIdentifier = identifier;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Mojang login failed: {ex}");
                            ShowNotification("Login failed", ex.Message);
                            return;
                        }
                        finally
                        {
                            playButton.Content = "Play";
                            playButton.IsEnabled = true;
                        }
                    }
                    else
                    {
                        try
                        {
                            playButton.IsEnabled = false;
                            playButton.Content = "Signing in...";

                            var (session, identifier) = await LoginHelper.LoginWithMojangInteractive();

                            account.Username = session.Username;
                            account.MojangIdentifier = identifier;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Mojang re-auth failed: {ex}");
                            ShowNotification("Re-authentication failed", ex.Message);
                            return;
                        }
                        finally
                        {
                            playButton.Content = "Play";
                            playButton.IsEnabled = true;
                        }
                    }
                }
                else
                {
                    var password = passwordBox.Password;

                    // try ely.by when password not empty and internet available
                    if (!string.IsNullOrWhiteSpace(password))
                    {
                        bool hasInternet = await NetworkHelper.InternetAvailable();

                        if (hasInternet)
                        {
                            try
                            {
                                await LoginHelper.LoginWithElyBy(account.Username, password);
                            }
                            catch (Exception ex) when (ex.Message == "INVALID_CREDENTIALS")
                            {
                                ShowNotification("Login failed", "Please verify your Ely.by account credentials.");
                                return;
                            }
                            catch
                            {
                            }
                        }
                    }

                    account.Password = string.IsNullOrWhiteSpace(password) ? null : password;
                    account.AccountType = PlayerAccountType.ElyBy;
                    account.MojangIdentifier = null;
                }
            }

            // refresh accounts
            AccountManager.UpdateAccount(account);
            LoadAccounts();
            accountComboBox.SelectedItem = accountItems.FirstOrDefault(x => x.Account?.Id == account.Id);
        }

        private static void ShowNotification(string title, string message)
        {
            NotificationHelper.Show(title, message);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // check if current page is not settingspage, if yes then navigate to settings page
            if (mainFrame.CurrentSourcePageType != typeof(SettingsPage))
                mainFrame.Navigate(typeof(SettingsPage), null, new SuppressNavigationTransitionInfo());
        }

        private void TitleBar_BackRequested(TitleBar sender, object args)
        {
            if (mainFrame.CanGoBack)
            {
                mainFrame.GoBack();
                MemoryOptimizer.ReduceMemory();
            }
        }

        private void SetDownloadProgress(double progress)
        {
            // set indeterminate when progress is 0 to indicate something is happening
            downloadProgressBar.IsIndeterminate = false;

            if (progress < downloadProgressValue)
                return;

            downloadProgressValue = Math.Min(progress, 100);
            downloadProgressBar.Value = downloadProgressValue;
        }

        private void SetWindowIcon()
        {
            // setting window icon in taskbar thumbnail, alt-tab and task manager
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "128.ico");
                if (File.Exists(iconPath))
                    AppWindow.SetIcon(iconPath);
            }
            catch
            {
                Debug.WriteLine("Failed to set window icon.");
            }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            backgroundImage.Source = null;
            overlayGrid.Background = null;

            var vm = VersionVM;
            // save settings
            SettingsManager.Current.ShowSnapshots = vm.ShowSnapshots;
            SettingsManager.Current.ShowFabric = vm.ShowFabric;
            SettingsManager.Current.ShowOld = vm.ShowOld;
            SettingsManager.SaveSettings();
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.IsEnabled = false;
                // open current installation location
                Process.Start("explorer.exe", SettingsManager.Current.GetActiveMinecraftPath());
                button.IsEnabled = true;
            }
        }

        private void ModsButton_Click(object sender, RoutedEventArgs e)
        {
            // check if no instances are selected
            if (SettingsManager.Current.InstancesEnabled && InstanceManager.GetSelectedInstance() == null)
            {
                return;
            }

            // check if current page is not modspage, if yes then navigate to modspage
            if (mainFrame.CurrentSourcePageType != typeof(ModsPage))
            {
                mainFrame.Navigate(typeof(ModsPage), null, new SuppressNavigationTransitionInfo());
                MemoryOptimizer.ReduceMemory();
            }
        }

        private async void BugReportButton_Click(object sender, RoutedEventArgs e)
        {
            // open github issues page to create a new issue
            var uri = new Uri("https://github.com/yoriichi111012/Yorii-Launcher/issues/new");
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }

        private void NotesButton_Click(object sender, RoutedEventArgs e)
        {
            // check if current page is not notespage, if yes then navigate to notespage
            if (mainFrame.CurrentSourcePageType != typeof(HomePage))
            {
                mainFrame.Navigate(typeof(HomePage), null, new SuppressNavigationTransitionInfo());
                MemoryOptimizer.ReduceMemory();
            }
        }

        private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (versionComboBox.SelectedItem != null)
            {
                var selectedVersion = versionComboBox.SelectedItem.ToString();

                if (string.IsNullOrWhiteSpace(selectedVersion))
                    return;

                // set version normally when instances disabled
                SettingsManager.Current.LastSavedVersion = selectedVersion;
                SettingsManager.Current.SelectedVersion = selectedVersion;

                // set version for particular instance
                if (SettingsManager.Current.InstancesEnabled)
                    InstanceManager.SetSelectedInstanceVersion(selectedVersion);

                SettingsManager.SaveSettings();
            }
        }

        private void VersionList_DropDownClosed(object sender, object e)
        {
            // version list can be really long so reduce memory won't hurt when it is closed
            MemoryOptimizer.ReduceMemory();
        }

        private sealed class AccountComboItem
        {
            public PlayerAccount? Account { get; init; }
            public bool IsAddNew { get; init; }
            public bool IsManagePlayers { get; init; }
            public string DisplayName => IsAddNew ? "Add player" : IsManagePlayers ? "Manage players" : Account?.Username ?? "";

            public static AccountComboItem AddNew { get; } = new() { IsAddNew = true };
            public static AccountComboItem ManagePlayers { get; } = new() { IsManagePlayers = true };

            public static AccountComboItem ForAccount(PlayerAccount account)
            {
                return new AccountComboItem { Account = account };
            }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
