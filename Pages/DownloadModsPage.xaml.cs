using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Yorii_Launcher.Helpers;
using Yorii_Launcher.Models;


namespace Yorii_Launcher.Pages
{
    public sealed partial class DownloadModsPage : Page
    {
        private static readonly HttpClient Http = new();
        private readonly ObservableCollection<OnlineModItem> OnlineMods = [];
        public DownloadModsPage()
        {
            InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;

            ModrinthList.ItemsSource = OnlineMods;

            _ = LoadFeaturedMods();
            MemoryOptimizer.ReduceMemory();
        }

        // search modrinth for mods matching the query
        private async void ModrinthSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            var query = textBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                await LoadFeaturedMods();
                return;
            }

            try
            {
                ModsErrorPanel.Visibility = Visibility.Collapsed;
                var minecraftVersion = SettingsManager.Current.GetCleanSelectedVersion();

                var url =
                    "https://api.modrinth.com/v2/search" +
                    $"?query={Uri.EscapeDataString(query)}" +
                    "&facets=[" +
                    "[\"categories:fabric\"]," +
                    "[\"project_type:mod\"]," +
                    $"[\"versions:{minecraftVersion}\"]" +
                    "]" +
                    "&limit=30";

                var json = await Http.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(json);

                var hits = doc.RootElement.GetProperty("hits");

                var newMods = new List<OnlineModItem>();

                foreach (var hit in hits.EnumerateArray())
                {
                    string title = hit.GetProperty("title").GetString() ?? "";

                    string description = hit.GetProperty("description").GetString() ?? "";

                    string slug = hit.GetProperty("slug").GetString() ?? "";

                    string iconUrl = "";

                    if (hit.TryGetProperty("icon_url", out var iconProp))
                    {
                        iconUrl = iconProp.GetString() ?? "";
                    }

                    BitmapImage? icon = null;

                    if (!string.IsNullOrWhiteSpace(iconUrl))
                    {
                        icon = new BitmapImage(
                            new Uri(iconUrl));
                    }

                    newMods.Add(new OnlineModItem
                    {
                        Title = title,
                        Description = description,
                        Slug = slug,
                        Icon = icon
                    });
                }

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

        // install mod and its dependencies
        private async Task InstallMod(string slug, HashSet<string>? installed = null)
        {
            installed ??= [];

            // prevent infinite loops
            if (installed.Contains(slug))
                return;

            installed.Add(slug);

            var minecraftVersion = SettingsManager.Current.GetCleanSelectedVersion();

            var loaders = Uri.EscapeDataString("[\"fabric\"]");

            var gameVersions = Uri.EscapeDataString($"[\"{minecraftVersion}\"]");

            var versionUrl =
                $"https://api.modrinth.com/v2/project/{slug}/version" +
                $"?loaders={loaders}" +
                $"&game_versions={gameVersions}";

            var versionJson = await Http.GetStringAsync(versionUrl);

            using JsonDocument versionDoc = JsonDocument.Parse(versionJson);

            var versions = versionDoc.RootElement;

            if (versions.GetArrayLength() == 0)
                return;

            JsonElement? selectedVersion = null;

            foreach (var version in versions.EnumerateArray())
            {
                bool compatible = await IsVersionCompatible(version);

                if (compatible)
                {
                    selectedVersion = version;
                    break;
                }
            }

            if (selectedVersion == null)
            {
                Debug.WriteLine("No compatible version found");

                return;
            }

            var latestVersion = selectedVersion.Value;

            // install dependencies first

            if (latestVersion.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dependency in dependencies.EnumerateArray())
                {
                    // only required dependencies
                    if (!dependency.TryGetProperty("dependency_type", out var typeProp))
                    {
                        continue;
                    }

                    var dependencyType = typeProp.GetString();

                    if (dependencyType != "required")
                        continue;

                    // get project slug/id
                    if (!dependency.TryGetProperty("project_id", out var projectProp))
                    {
                        continue;
                    }

                    var dependencyProjectId = projectProp.GetString();

                    if (string.IsNullOrWhiteSpace(dependencyProjectId))
                    {
                        continue;
                    }

                    // convert project id -> slug
                    var projectJson = await Http.GetStringAsync($"https://api.modrinth.com/v2/project/{dependencyProjectId}");

                    using JsonDocument projectDoc = JsonDocument.Parse(projectJson);

                    var dependencySlug = projectDoc.RootElement.GetProperty("slug").GetString();

                    if (!string.IsNullOrWhiteSpace(dependencySlug))
                    {
                        await InstallMod(dependencySlug, installed);
                    }
                }
            }

            var files = latestVersion.GetProperty("files");

            if (files.GetArrayLength() == 0)
                return;

            JsonElement? selectedFile = null;

            // Prefer primary jar
            foreach (var f in files.EnumerateArray())
            {
                bool isPrimary = f.TryGetProperty("primary", out var primaryProp) && primaryProp.GetBoolean();

                var filename = f.GetProperty("filename").GetString();

                if (isPrimary && filename?.EndsWith(".jar") == true)
                {
                    selectedFile = f;
                    break;
                }
            }

            // fallback to first jar
            if (selectedFile == null)
            {
                foreach (var f in files.EnumerateArray())
                {
                    var filename = f.GetProperty("filename").GetString();

                    if (filename?.EndsWith(".jar") == true)
                    {
                        selectedFile = f;
                        break;
                    }
                }
            }

            if (selectedFile == null)
                return;

            var file = selectedFile.Value;

            var downloadUrl = file.GetProperty("url").GetString();

            var fileName = file.GetProperty("filename").GetString();

            var minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();

            var modsFolder = Path.Combine(minecraftPath, "mods");

            Directory.CreateDirectory(modsFolder);

            if (string.IsNullOrEmpty(fileName))
                return;

            var destination = Path.Combine(modsFolder, fileName);

            // skip if already exists
            if (File.Exists(destination))
                return;

            using var response = await Http.GetAsync(downloadUrl);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();

            await using var fileStream = File.Create(destination);

            await stream.CopyToAsync(fileStream);

            await fileStream.FlushAsync();
        }

        private async void InstallMod_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (button.Tag is not OnlineModItem mod)
                return;

            try
            {
                await InstallMod(mod.Slug);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                NotificationHelper.Show("Mod install failed", $"Could not install {mod.Title}. Check your internet connection.");
            }
        }

        // check if version works with current mc version + fabric
        private static async Task<bool> IsVersionCompatible(JsonElement version)
        {
            var currentMcVersion = SettingsManager.Current.GetCleanSelectedVersion();
            if (version.TryGetProperty("game_versions", out var gameVersions))
            {
                bool supportsMc = gameVersions.EnumerateArray()
                    .Any(v => v.GetString() == currentMcVersion);

                if (!supportsMc) return false;
            }

            // check it supports fabric
            if (version.TryGetProperty("loaders", out var loaders))
            {
                bool supportsFabric = loaders.EnumerateArray()
                    .Any(l => l.GetString()?.ToLower() == "fabric");

                if (!supportsFabric) return false;
            }

            return true;
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
                    await InstallSpecificVersion(version.VersionId);
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

        // install specific version by id, no dependency stuff
        private static async Task InstallSpecificVersion(string versionId)
        {
            var json = await Http.GetStringAsync($"https://api.modrinth.com/v2/version/{versionId}");

            using JsonDocument doc = JsonDocument.Parse(json);

            var files = doc.RootElement.GetProperty("files");

            if (files.GetArrayLength() == 0)
                return;

            JsonElement? selectedFile = null;

            // prefer primary file

            foreach (var f in files.EnumerateArray())
            {
                if (f.TryGetProperty("primary", out var primaryProp) && primaryProp.GetBoolean())
                {
                    var primaryName = f.GetProperty("filename").GetString();

                    // make sure primary file is actually a jar
                    if (primaryName?.EndsWith(".jar") == true)
                    {
                        selectedFile = f;
                        break;
                    }
                }
            }

            // fallback to first jar

            if (selectedFile == null)
            {
                foreach (var f in files.EnumerateArray())
                {
                    var filename = f.GetProperty("filename").GetString();

                    if (filename?.EndsWith(".jar") == true)
                    {
                        selectedFile = f;
                        break;
                    }
                }
            }

            // no usable mod file found
            if (selectedFile == null)
                return;

            var file = selectedFile.Value;

            var downloadUrl = file.GetProperty("url").GetString();

            var fileName = file.GetProperty("filename").GetString();

            if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            var minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();

            if (string.IsNullOrWhiteSpace(minecraftPath))
                return;

            var modsFolder = Path.Combine(minecraftPath, "mods");

            Directory.CreateDirectory(modsFolder);

            var destination = Path.Combine(modsFolder, fileName);

            // avoid redownloading existing file
            if (File.Exists(destination))
                return;

            using var response = await Http.GetAsync(downloadUrl);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();

            await using var fileStream = File.Create(destination);

            await stream.CopyToAsync(fileStream);

            await fileStream.FlushAsync();
        }

        // fetch compatible versions from modrinth
        private static async Task<List<OnlineModItem>> GetVersions(string slug)
        {
            var minecraftVersion = SettingsManager.Current.GetCleanSelectedVersion();

            var loaders =
                Uri.EscapeDataString("[\"fabric\"]");

            var gameVersions =
                Uri.EscapeDataString(
                    $"[\"{minecraftVersion}\"]");

            var url =
                $"https://api.modrinth.com/v2/project/{slug}/version" +
                $"?loaders={loaders}" +
                $"&game_versions={gameVersions}";

            var json = await Http.GetStringAsync(url);

            using JsonDocument doc = JsonDocument.Parse(json);

            var versions = new List<OnlineModItem>();

            foreach (var version in doc.RootElement.EnumerateArray())
            {
                if (!version.TryGetProperty(
                    "files",
                    out var files))
                {
                    continue;
                }

                bool hasJar = false;

                foreach (var file in files.EnumerateArray())
                {
                    var filename = file.GetProperty("filename").GetString();

                    if (filename?.EndsWith(".jar") == true)
                    {
                        hasJar = true;
                        break;
                    }
                }

                if (!hasJar)
                    continue;

                string versionName =
                    version.GetProperty("name")
                           .GetString() ?? "";

                string versionId =
                    version.GetProperty("id")
                           .GetString() ?? "";

                // trim long names
                if (versionName.Length > 60)
                {
                    versionName =
                        versionName[..60] + "...";
                }

                versions.Add(new OnlineModItem
                {
                    VersionName = versionName,
                    VersionId = versionId,
                    Slug = slug
                });
            }

            versions = versions
                .Take(15)
                .ToList();

            return versions;
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

                    var newMods = new List<OnlineModItem>();
                    var minecraftVersion = SettingsManager.Current.GetCleanSelectedVersion();
                    var tasks = featuredMods.Select(async modQuery =>
                    {

                        var url = "https://api.modrinth.com/v2/search" +
                            $"?query={Uri.EscapeDataString(modQuery)}" +
                            "&facets=[" +
                            "[\"categories:fabric\"]," +
                            "[\"project_type:mod\"]," +
                            $"[\"versions:{minecraftVersion}\"]" +
                            "]" +
                            "&limit=1";

                        var json = await Http.GetStringAsync(url);

                        using var doc = JsonDocument.Parse(json);
                        var hits = doc.RootElement.GetProperty("hits");

                        if (hits.GetArrayLength() == 0)
                            return null;

                        var hit = hits[0];

                        return new OnlineModItem
                        {
                            Title = hit.GetProperty("title").GetString() ?? "",
                            Description = hit.GetProperty("description").GetString() ?? "",
                            Slug = hit.GetProperty("slug").GetString() ?? "",
                            Icon = hit.TryGetProperty("icon_url", out var iconProp)
                                ? new BitmapImage(new Uri(iconProp.GetString() ?? ""))
                                : null
                        };
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
