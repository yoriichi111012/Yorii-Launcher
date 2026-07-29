using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Yorii_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Yorii_Launcher.Helpers
{
    public enum ModrinthProjectKind
    {
        Mod,
        ResourcePack,
        Modpack
    }

    public static class ModrinthHelper
    {
        private static readonly Dictionary<string, string> responseCache = [];
        private static readonly List<string> cacheOrder = [];
        private const int MaxCacheSize = 100;
        private const int ManagedFileIconSize = 40;

        public static string GetProjectType(ModrinthProjectKind kind) => kind switch
        {
            ModrinthProjectKind.Mod => "mod",
            ModrinthProjectKind.ResourcePack => "resourcepack",
            ModrinthProjectKind.Modpack => "modpack",
            _ => "mod"
        };

        public static string GetModrinthPath(ModrinthProjectKind kind) => kind switch
        {
            ModrinthProjectKind.Mod => "mod",
            ModrinthProjectKind.ResourcePack => "resourcepack",
            ModrinthProjectKind.Modpack => "modpack",
            _ => "mod"
        };

        public static string GetInstallFolder(ModrinthProjectKind kind)
        {
            var minecraftPath = SettingsManager.Current.GetActiveMinecraftPath();
            var folderName = kind switch
            {
                ModrinthProjectKind.Mod => "mods",
                ModrinthProjectKind.ResourcePack => "resourcepacks",
                ModrinthProjectKind.Modpack => "modpacks",
                _ => "mods"
            };

            return Path.Combine(minecraftPath, folderName);
        }

        public static string GetExpectedExtension(ModrinthProjectKind kind) => kind switch
        {
            ModrinthProjectKind.Mod => ".jar",
            ModrinthProjectKind.ResourcePack => ".zip",
            ModrinthProjectKind.Modpack => ".mrpack",
            _ => ".jar"
        };

        public static string? GetLoader(ModrinthProjectKind kind) => kind switch
        {
            ModrinthProjectKind.Mod => "fabric",
            ModrinthProjectKind.ResourcePack => "minecraft",
            _ => null
        };

        public static async Task<List<OnlineModItem>> SearchProjectsAsync(ModrinthProjectKind kind, string query, int limit = 30)
        {
            var projectType = GetProjectType(kind);
            var minecraftVersion = SettingsManager.Current.GetCleanSelectedVersion();
            var facets = new List<string>
            {
                $"[\"project_type:{projectType}\"]"
            };

            if (kind != ModrinthProjectKind.ResourcePack || !SettingsManager.Current.ExperimentalResourcePackAnyVersion)
                facets.Add($"[\"versions:{minecraftVersion}\"]");

            var loader = GetLoader(kind);
            if (!string.IsNullOrWhiteSpace(loader))
                facets.Add($"[\"categories:{loader}\"]");

            var url =
                "https://api.modrinth.com/v2/search" +
                $"?query={Uri.EscapeDataString(query)}" +
                $"&facets=[{string.Join(",", facets)}]" +
                $"&limit={limit}";

            var json = await GetCachedStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var hits = doc.RootElement.GetProperty("hits");

            var projects = new List<OnlineModItem>();

            foreach (var hit in hits.EnumerateArray())
            {
                var iconUrl = hit.TryGetProperty("icon_url", out var iconProp)
                    ? iconProp.GetString() ?? ""
                    : "";

                projects.Add(new OnlineModItem
                {
                    Title = hit.GetProperty("title").GetString() ?? "",
                    Description = hit.GetProperty("description").GetString() ?? "",
                    Slug = hit.GetProperty("slug").GetString() ?? "",
                    Icon = CreateRemoteIcon(iconUrl)
                });
            }

            return projects;
        }

        public static async Task<List<OnlineModItem>> GetVersionsAsync(ModrinthProjectKind kind, string slug, int limit = 15)
        {
            var url = BuildVersionsUrl(kind, slug);
            var json = await GetCachedStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            var versions = new List<OnlineModItem>();
            var extension = GetExpectedExtension(kind);

            foreach (var version in doc.RootElement.EnumerateArray())
            {
                if (!version.TryGetProperty("files", out var files))
                    continue;

                if (!files.EnumerateArray().Any(file => IsMatchingFile(file, extension)))
                    continue;

                var versionName = version.GetProperty("name").GetString() ?? "";
                if (versionName.Length > 60)
                    versionName = versionName[..60] + "...";

                versions.Add(new OnlineModItem
                {
                    VersionName = versionName,
                    VersionId = version.GetProperty("id").GetString() ?? "",
                    Slug = slug
                });
            }

            return versions.Take(limit).ToList();
        }

        public static Task InstallLatestProjectAsync(ModrinthProjectKind kind, string slug)
        {
            return InstallLatestProjectAsync(kind, slug, []);
        }

        private static async Task InstallLatestProjectAsync(ModrinthProjectKind kind, string slug, HashSet<string> installed)
        {
            var key = $"{kind}:{slug}";
            if (!installed.Add(key))
                return;

            var url = BuildVersionsUrl(kind, slug);
            var json = await GetCachedStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            var versions = doc.RootElement;
            if (versions.GetArrayLength() == 0)
                return;

            JsonElement? selectedVersion = null;
            foreach (var version in versions.EnumerateArray())
            {
                if (IsVersionCompatible(kind, version) && TrySelectFile(kind, version, out _))
                {
                    selectedVersion = version;
                    break;
                }
            }

            // fallback: if no version matches the current mc version, grab the
            // latest one that has files (resource packs often work fine across versions)
            if (selectedVersion == null &&
                kind == ModrinthProjectKind.ResourcePack &&
                SettingsManager.Current.ExperimentalResourcePackAnyVersion)
            {
                foreach (var version in versions.EnumerateArray())
                {
                    if (TrySelectFile(kind, version, out _))
                    {
                        selectedVersion = version;
                        break;
                    }
                }
            }

            if (selectedVersion == null)
                return;

            if (kind == ModrinthProjectKind.Mod)
                await InstallRequiredDependenciesAsync(selectedVersion.Value, installed);

            await InstallVersionElementAsync(kind, selectedVersion.Value);
        }

        public static async Task InstallVersionAsync(ModrinthProjectKind kind, string versionId)
        {
            await InstallVersionAsync(kind, versionId, []);
        }

        private static async Task InstallVersionAsync(ModrinthProjectKind kind, string versionId, HashSet<string> installed)
        {
            var json = await GetCachedStringAsync($"https://api.modrinth.com/v2/version/{versionId}");
            using var doc = JsonDocument.Parse(json);

            if (kind == ModrinthProjectKind.Mod)
                await InstallRequiredDependenciesAsync(doc.RootElement, installed);

            await InstallVersionElementAsync(kind, doc.RootElement);
        }

        private static async Task InstallRequiredDependenciesAsync(JsonElement version, HashSet<string> installed)
        {
            if (!version.TryGetProperty("dependencies", out var dependencies))
                return;

            foreach (var dependency in dependencies.EnumerateArray())
            {
                if (!dependency.TryGetProperty("dependency_type", out var typeProp))
                    continue;

                if (typeProp.GetString() != "required")
                    continue;

                if (dependency.TryGetProperty("version_id", out var versionProp))
                {
                    var versionId = versionProp.GetString();
                    if (!string.IsNullOrWhiteSpace(versionId))
                    {
                        var key = $"version:{versionId}";
                        if (installed.Add(key))
                            await InstallVersionAsync(ModrinthProjectKind.Mod, versionId, installed);
                    }
                    continue;
                }

                if (!dependency.TryGetProperty("project_id", out var projectProp))
                    continue;

                var projectId = projectProp.GetString();
                if (string.IsNullOrWhiteSpace(projectId))
                    continue;

                try
                {
                    var projectJson = await GetCachedStringAsync($"https://api.modrinth.com/v2/project/{projectId}");
                    using var projectDoc = JsonDocument.Parse(projectJson);
                    var dependencySlug = projectDoc.RootElement.GetProperty("slug").GetString();

                    if (!string.IsNullOrWhiteSpace(dependencySlug))
                        await InstallLatestProjectAsync(ModrinthProjectKind.Mod, dependencySlug, installed);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Dependency install failed: {ex}");
                }
            }
        }

        private static async Task InstallVersionElementAsync(ModrinthProjectKind kind, JsonElement version)
        {
            if (!TrySelectFile(kind, version, out var file))
                return;

            var downloadUrl = file.GetProperty("url").GetString();
            var fileName = file.GetProperty("filename").GetString();

            if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(fileName))
                return;

            var folder = GetInstallFolder(kind);
            Directory.CreateDirectory(folder);

            var destination = Path.Combine(folder, fileName);
            if (File.Exists(destination))
                return;

            using var response = await HttpService.DownloadClient.GetAsync(downloadUrl);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(destination);
            await stream.CopyToAsync(fileStream);
            await fileStream.FlushAsync();
        }

        private static string BuildVersionsUrl(ModrinthProjectKind kind, string slug)
        {
            var minecraftVersion = SettingsManager.Current.GetCleanSelectedVersion();
            var url = $"https://api.modrinth.com/v2/project/{slug}/version";

            if (kind != ModrinthProjectKind.ResourcePack || !SettingsManager.Current.ExperimentalResourcePackAnyVersion)
            {
                var gameVersions = Uri.EscapeDataString($"[\"{minecraftVersion}\"]");
                url += $"?game_versions={gameVersions}";
            }

            var loader = GetLoader(kind);
            if (!string.IsNullOrWhiteSpace(loader))
                url += (url.Contains('?') ? "&" : "?") + $"loaders={Uri.EscapeDataString($"[\"{loader}\"]")}";

            return url;
        }

        private static bool IsVersionCompatible(ModrinthProjectKind kind, JsonElement version)
        {
            var currentMcVersion = SettingsManager.Current.GetCleanSelectedVersion();

            if (version.TryGetProperty("game_versions", out var gameVersions) &&
                !gameVersions.EnumerateArray().Any(v => v.GetString() == currentMcVersion))
            {
                return false;
            }

            var loader = GetLoader(kind);
            if (!string.IsNullOrWhiteSpace(loader) &&
                version.TryGetProperty("loaders", out var loaders) &&
                !loaders.EnumerateArray().Any(l => string.Equals(l.GetString(), loader, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }

        private static bool TrySelectFile(ModrinthProjectKind kind, JsonElement version, out JsonElement file)
        {
            file = default;

            if (!version.TryGetProperty("files", out var files) || files.GetArrayLength() == 0)
                return false;

            var extension = GetExpectedExtension(kind);

            foreach (var candidate in files.EnumerateArray())
            {
                if (candidate.TryGetProperty("primary", out var primaryProp) &&
                    primaryProp.GetBoolean() &&
                    IsMatchingFile(candidate, extension))
                {
                    file = candidate;
                    return true;
                }
            }

            foreach (var candidate in files.EnumerateArray())
            {
                if (IsMatchingFile(candidate, extension))
                {
                    file = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsMatchingFile(JsonElement file, string extension)
        {
            var fileName = file.GetProperty("filename").GetString();
            return fileName?.EndsWith(extension, StringComparison.OrdinalIgnoreCase) == true;
        }

        public static BitmapImage? CreateRemoteIcon(string iconUrl)
        {
            if (string.IsNullOrWhiteSpace(iconUrl))
                return null;

            return Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri)
                ? new BitmapImage(uri)
                : null;
        }

        public static async Task<ManagedFileItem> ReadManagedFileAsync(string file, ModrinthProjectKind kind)
        {
            var item = new ManagedFileItem
            {
                Name = Path.GetFileNameWithoutExtension(file.Replace(".disabled", "")),
                Version = Path.GetExtension(file).Equals(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? "Disabled"
                    : "Installed",
                FilePath = file,
                ProjectType = GetProjectType(kind),
                IsEnabled = !file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            };

            if (kind == ModrinthProjectKind.ResourcePack)
            {
                item.Icon = await ReadZipIcon(file, "pack.png");
                return item;
            }

            if (kind != ModrinthProjectKind.Modpack)
                return item;

            try
            {
                using var archive = ZipFile.OpenRead(file);
                var indexEntry = archive.GetEntry("modrinth.index.json");
                if (indexEntry == null)
                    return item;

                using var stream = indexEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("name", out var nameProp))
                    item.Name = nameProp.GetString() ?? item.Name;

                if (root.TryGetProperty("versionId", out var versionProp))
                    item.Version = versionProp.GetString() ?? item.Version;
            }
            catch
            {
            }

            return item;
        }

        // LRU cache with max 100 entries, keeps repeated api calls fast
        private static async Task<string> GetCachedStringAsync(string url)
        {
            if (responseCache.TryGetValue(url, out var cached))
            {
                // move to back so we know it was recently used
                cacheOrder.Remove(url);
                cacheOrder.Add(url);
                return cached;
            }

            var json = await HttpService.Client.GetStringAsync(url);

            // evict oldest entry when cache is full
            if (responseCache.Count >= MaxCacheSize)
            {
                var oldest = cacheOrder[0];
                cacheOrder.RemoveAt(0);
                responseCache.Remove(oldest);
            }

            responseCache[url] = json;
            cacheOrder.Add(url);
            return json;
        }

        private static async Task<ImageSource?> ReadZipIcon(string file, string iconName)
        {
            try
            {
                using var archive = ZipFile.OpenRead(file);
                var iconEntry = archive.Entries.FirstOrDefault(entry =>
                    entry.FullName.Equals(iconName, StringComparison.OrdinalIgnoreCase));

                if (iconEntry == null)
                    return null;

                using var iconStream = iconEntry.Open();
                using var memory = new MemoryStream();

                await iconStream.CopyToAsync(memory);
                memory.Position = 0;

                return await CreateManagedFileIconAsync(memory);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<ImageSource> CreateManagedFileIconAsync(MemoryStream memory)
        {
            var randomAccessStream = memory.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);

            if (decoder.PixelWidth <= ManagedFileIconSize || decoder.PixelHeight <= ManagedFileIconSize)
                return await CreateNearestNeighborIconAsync(decoder);

            memory.Position = 0;
            randomAccessStream = memory.AsRandomAccessStream();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(randomAccessStream);
            return bitmap;
        }

        private static async Task<WriteableBitmap> CreateNearestNeighborIconAsync(BitmapDecoder decoder)
        {
            var sourceWidth = (int)decoder.PixelWidth;
            var sourceHeight = (int)decoder.PixelHeight;
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var sourcePixels = pixelData.DetachPixelData();
            var targetPixels = new byte[ManagedFileIconSize * ManagedFileIconSize * 4];
            var scale = Math.Max(
                ManagedFileIconSize / (double)sourceWidth,
                ManagedFileIconSize / (double)sourceHeight);
            var scaledWidth = sourceWidth * scale;
            var scaledHeight = sourceHeight * scale;
            var offsetX = (scaledWidth - ManagedFileIconSize) / 2;
            var offsetY = (scaledHeight - ManagedFileIconSize) / 2;

            for (var y = 0; y < ManagedFileIconSize; y++)
            {
                var sourceY = Math.Clamp((int)((y + offsetY) / scale), 0, sourceHeight - 1);

                for (var x = 0; x < ManagedFileIconSize; x++)
                {
                    var sourceX = Math.Clamp((int)((x + offsetX) / scale), 0, sourceWidth - 1);
                    var sourceIndex = ((sourceY * sourceWidth) + sourceX) * 4;
                    var targetIndex = ((y * ManagedFileIconSize) + x) * 4;

                    targetPixels[targetIndex] = sourcePixels[sourceIndex];
                    targetPixels[targetIndex + 1] = sourcePixels[sourceIndex + 1];
                    targetPixels[targetIndex + 2] = sourcePixels[sourceIndex + 2];
                    targetPixels[targetIndex + 3] = sourcePixels[sourceIndex + 3];
                }
            }

            var bitmap = new WriteableBitmap(ManagedFileIconSize, ManagedFileIconSize);

            using (var pixelStream = bitmap.PixelBuffer.AsStream())
            {
                await pixelStream.WriteAsync(targetPixels);
            }

            return bitmap;
        }
    }
}
