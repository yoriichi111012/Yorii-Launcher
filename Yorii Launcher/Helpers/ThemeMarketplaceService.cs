using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers;

public sealed class ThemeMarketplaceService
{
    public const string DefaultCatalogUrl =
        "https://raw.githubusercontent.com/yoriichi111012/yorii-themes/main/themes.yaml";

    // shared pooled connection — no per-service sockets, 8s cap instead of 100s hang
    private static readonly HttpClient HttpClient = HttpService.Client;
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromSeconds(20);

    private static readonly string CatalogCacheDir = Path.Combine(
        ApplicationData.Current.LocalFolder.Path, "ThemePreviewCache");
    private static readonly string CatalogCachePath = Path.Combine(CatalogCacheDir, "catalog.yaml");
    private static readonly string CatalogCacheMetaPath = Path.Combine(CatalogCacheDir, "catalog.cache");
    private static readonly string CatalogPendingPath = Path.Combine(CatalogCacheDir, "catalog.pending.yaml");

    private static IReadOnlyList<ThemeCatalogEntry>? _cachedCatalog;
    private static DateTime _catalogFetchedAt;

    public static void InvalidateCatalogCache()
    {
        _cachedCatalog = null;
        _catalogFetchedAt = DateTime.MinValue;
        try { if (File.Exists(CatalogCachePath)) File.Delete(CatalogCachePath); } catch { }
        try { if (File.Exists(CatalogCacheMetaPath)) File.Delete(CatalogCacheMetaPath); } catch { }
    }

    public async Task<IReadOnlyList<ThemeCatalogEntry>> GetCatalogAsync(
        string? catalogUrl = null,
        CancellationToken cancellationToken = default)
    {
        var url = catalogUrl ?? DefaultCatalogUrl;
        // raw.githubusercontent.com is cdn-cached for ~5 min, so after a publish
        // the fresh themes.yaml is already on github but the cdn still serves
        // the old file — bust it with a timestamp and no-cache header
        if (string.Equals(url, DefaultCatalogUrl, StringComparison.OrdinalIgnoreCase))
            url = $"{url}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        req.Headers.Pragma.Add(new System.Net.Http.Headers.NameValueHeaderValue("no-cache"));
        using var resp = await HttpClient.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();
        var yaml = await resp.Content.ReadAsStringAsync(cancellationToken);
        var catalog = LauncherYaml.Deserialize<ThemeCatalog>(yaml);
        if (catalog?.Themes.Count > 0)
            return catalog.Themes;

        return LauncherYaml.Deserialize<List<ThemeCatalogEntry>>(yaml) ?? [];
    }

    public async Task<IReadOnlyList<ThemeCatalogEntry>> GetCatalogCachedAsync(
        CancellationToken cancellationToken = default)
    {
        // 1. fresh in-memory catalog
        if (_cachedCatalog is not null && DateTime.UtcNow - _catalogFetchedAt < CatalogTtl)
            return _cachedCatalog;

        // 2. fresh on-disk catalog
        var diskCatalog = await TryLoadDiskCatalogAsync(cancellationToken);
        var diskTimestamp = GetCatalogCacheTimestamp();
        if (diskCatalog is not null && DateTime.UtcNow - diskTimestamp < CatalogTtl)
        {
            _cachedCatalog = diskCatalog;
            _catalogFetchedAt = diskTimestamp;
            return _cachedCatalog;
        }

        // 3. fetch from network, falling back to stale disk cache when offline
        try
        {
            var catalog = await GetCatalogAsync(cancellationToken: cancellationToken);
            catalog = await MergePendingAsync(catalog, cancellationToken);
            _cachedCatalog = catalog;
            _catalogFetchedAt = DateTime.UtcNow;
            await SaveDiskCatalogAsync(catalog, cancellationToken);
            return catalog;
        }
        catch (HttpRequestException) when (diskCatalog is not null)
        {
            // network failed — still merge any pending local publishes so a
            // theme published while offline isn't lost when coming back
            var merged = await MergePendingAsync(diskCatalog, cancellationToken);
            _cachedCatalog = merged;
            _catalogFetchedAt = diskTimestamp;
            return _cachedCatalog;
        }
    }

    // optimistic local update: insert or replace a catalog entry without any
    // network round-trip so a freshly published theme appears immediately
    // also tracked in a pending file so the entry survives the next network
    // fetch when the cdn is still serving the stale themes.yaml
    public static void ApplyLocalCatalogUpdate(ThemeCatalogEntry entry)
    {
        var list = _cachedCatalog is not null ? new List<ThemeCatalogEntry>(_cachedCatalog) : [];
        list.RemoveAll(e => string.Equals(e.Theme, entry.Theme, StringComparison.OrdinalIgnoreCase));
        list.Add(entry);
        SetCatalog(list);
        _ = SavePendingAsync(entry);
    }

    public static void ApplyLocalCatalogRemoval(string themeName)
    {
        if (_cachedCatalog is null) return;
        var list = new List<ThemeCatalogEntry>(_cachedCatalog);
        list.RemoveAll(e => string.Equals(e.Theme, themeName, StringComparison.OrdinalIgnoreCase));
        SetCatalog(list);
        _ = RemovePendingAsync(themeName);
    }

    private static async void SetCatalog(List<ThemeCatalogEntry> catalog)
    {
        _cachedCatalog = catalog;
        _catalogFetchedAt = DateTime.UtcNow;
        await SaveDiskCatalogAsync(catalog, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<ThemeCatalogEntry>?> TryLoadDiskCatalogAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(CatalogCachePath))
                return null;

            var list = LauncherYaml.Deserialize<List<ThemeCatalogEntry>>(
                await File.ReadAllTextAsync(CatalogCachePath, cancellationToken));
            return list is { Count: > 0 } ? list : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveDiskCatalogAsync(
        IReadOnlyList<ThemeCatalogEntry> catalog,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(CatalogCacheDir);
            await File.WriteAllTextAsync(
                CatalogCachePath,
                LauncherYaml.Serialize(catalog.ToList()),
                cancellationToken);
            await File.WriteAllTextAsync(
                CatalogCacheMetaPath,
                DateTime.UtcNow.Ticks.ToString(),
                cancellationToken);
        }
        catch
        {
            // cache is best-effort
        }
    }

    private static DateTime GetCatalogCacheTimestamp()
    {
        try
        {
            if (File.Exists(CatalogCacheMetaPath))
                return new DateTime(long.Parse(File.ReadAllText(CatalogCacheMetaPath)), DateTimeKind.Utc);
        }
        catch
        {
        }

        return DateTime.MinValue;
    }

    private static async Task SavePendingAsync(ThemeCatalogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(CatalogCacheDir);
            var pending = await LoadPendingAsync();
            pending.RemoveAll(e => string.Equals(e.Theme, entry.Theme, StringComparison.OrdinalIgnoreCase));
            pending.Add(entry);
            await File.WriteAllTextAsync(CatalogPendingPath, LauncherYaml.Serialize(pending));
        }
        catch { }
    }

    private static async Task RemovePendingAsync(string themeName)
    {
        try
        {
            var pending = await LoadPendingAsync();
            var removed = pending.RemoveAll(e => string.Equals(e.Theme, themeName, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return;
            if (pending.Count == 0)
                try { File.Delete(CatalogPendingPath); } catch { }
            else
                await File.WriteAllTextAsync(CatalogPendingPath, LauncherYaml.Serialize(pending));
        }
        catch { }
    }

    private static async Task<List<ThemeCatalogEntry>> LoadPendingAsync()
    {
        try
        {
            if (!File.Exists(CatalogPendingPath)) return [];
            var yaml = await File.ReadAllTextAsync(CatalogPendingPath);
            return LauncherYaml.Deserialize<List<ThemeCatalogEntry>>(yaml) ?? [];
        }
        catch { return []; }
    }

    private static async Task<IReadOnlyList<ThemeCatalogEntry>> MergePendingAsync(
        IReadOnlyList<ThemeCatalogEntry> catalog,
        CancellationToken cancellationToken)
    {
        var pending = await LoadPendingAsync();
        if (pending.Count == 0) return catalog;

        var dict = catalog.ToDictionary(e => e.Theme, StringComparer.OrdinalIgnoreCase);
        var merged = new List<ThemeCatalogEntry>(catalog);
        var stillPending = new List<ThemeCatalogEntry>();

        foreach (var p in pending)
        {
            if (dict.ContainsKey(p.Theme))
                continue; // cdn caught up, pending fulfilled
            merged.Add(p);
            stillPending.Add(p);
        }

        // persist only still-pending entries
        try
        {
            if (stillPending.Count == 0)
                try { File.Delete(CatalogPendingPath); } catch { }
            else if (stillPending.Count != pending.Count)
                await File.WriteAllTextAsync(CatalogPendingPath, LauncherYaml.Serialize(stillPending), cancellationToken);
        }
        catch { }

        return merged;
    }

    public async Task<ThemeDetails?> GetDetailsAsync(
        ThemeCatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        var detailsUrl = entry.DetailsUrl ?? GetSiblingUrl(entry.Url, "theme-details.yaml");
        try
        {
            return LauncherYaml.Deserialize<ThemeDetails>(
                await HttpClient.GetStringAsync(detailsUrl, cancellationToken));
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<string> InstallAsync(
        ThemeCatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        var item = DownloadManager.Add($"Theme · {entry.Theme}", DownloadKind.Theme);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, item.Token);
        var ct = linkedCts.Token;

        try
        {
            if (string.IsNullOrWhiteSpace(entry.Url))
                throw new InvalidDataException("The marketplace entry does not define url.");

            var theme = LauncherYaml.Deserialize<ThemeDefinition>(
                await HttpClient.GetStringAsync(entry.Url, ct))
                ?? throw new InvalidDataException("The theme YAML is empty or invalid.");

            var themeFolder = Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                "Themes",
                GetSafeDirectoryName(entry.Theme));
            Directory.CreateDirectory(themeFolder);

            var localThemePath = Path.Combine(themeFolder, "theme.yaml");
            File.WriteAllText(localThemePath, LauncherYaml.Serialize(theme));

            // details and background are independent, fetch them together
            var detailsTask = GetDetailsAsync(entry, ct);
            var backgroundTask = DownloadBackgroundAsync(entry, themeFolder, ct);
            var details = await detailsTask;
            if (details is not null)
                File.WriteAllText(Path.Combine(themeFolder, "theme-details.yaml"), LauncherYaml.Serialize(details));
            await backgroundTask;

            InstalledThemes.NotifyChanged();
            item.Complete();
            return themeFolder;
        }
        catch (OperationCanceledException)
        {
            item.Cancel();
            throw;
        }
        catch (Exception ex)
        {
            item.Fail(ex.Message);
            throw;
        }
    }

    public void ApplyTheme(string themeFolder)
    {
        var localThemePath = Path.Combine(themeFolder, "theme.yaml");
        if (!File.Exists(localThemePath))
            return;

        var theme = LauncherYaml.Deserialize<ThemeDefinition>(File.ReadAllText(localThemePath));
        if (theme is null)
            return;

        var backgroundDir = themeFolder;
        string? localBackgroundPath = null;
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            var bgPath = Path.Combine(backgroundDir, "background" + ext);
            if (File.Exists(bgPath))
            {
                localBackgroundPath = bgPath;
                break;
            }
        }

        ThemeManager.ApplyThemeDefinition(theme, localBackgroundPath);
        ThemeManager.Current.ActiveThemeFolder = Path.GetFileName(themeFolder);
        ThemeManager.SaveSettings();

        ThemeHelper.ApplySavedTheme();
        AccentThemeManager.ApplySavedAccent();
        MainWindow.Instance?.ApplyBackgroundSettings();
    }

    // previews render ~200px tall; decoding at 640px keeps quality for dpi
    // scaling while avoiding full-resolution decodes of large source images
    private const int PreviewDecodeWidth = 640;
    private const int MaxCachedImages = 30;

    private static readonly string CacheDir = Path.Combine(
        ApplicationData.Current.LocalFolder.Path, "ThemePreviewCache");

    private static readonly object imageLock = new();
    private static readonly List<string> imageOrder = [];
    private static readonly Dictionary<string, BitmapImage> ImageCache = new(StringComparer.Ordinal);

    public async Task<BitmapImage?> GetPreviewImageAsync(
        ThemeCatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(CacheDir);

        var candidates = new[]
        {
            GetSiblingUrl(entry.Url, "preview.jpg"),
            GetSiblingUrl(entry.Url, "preview.png"),
            GetSiblingUrl(entry.Url, "preview.jpeg")
        };

        // check caches for all candidates up-front; only hit the network for
        // the ones with nothing cached
        foreach (var url in candidates)
        {
            lock (imageLock)
            {
                if (ImageCache.TryGetValue(url, out var cached))
                {
                    // lru touch
                    imageOrder.Remove(url);
                    imageOrder.Add(url);
                    return cached;
                }
            }
        }

        // race the uncached candidates in parallel; first successful response wins
        var tasks = candidates.Select(url => Task.Run(async () =>
        {
            var cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(url)));
            var cachePath = Path.Combine(CacheDir, cacheKey + ".jpg");

            byte[] bytes;
            if (File.Exists(cachePath))
            {
                bytes = await File.ReadAllBytesAsync(cachePath, cancellationToken);
            }
            else
            {
                bytes = await HttpClient.GetByteArrayAsync(url, cancellationToken);
                await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
            }

            return (url, bytes);
        }, cancellationToken)).ToList();

        while (tasks.Count > 0)
        {
            var finished = await Task.WhenAny(tasks);
            tasks.Remove(finished);
            try
            {
                var result = await finished;
                var (url, bytes) = (result.Item1, result.Item2);
                var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.DecodePixelWidth = PreviewDecodeWidth;
                await image.SetSourceAsync(stream.AsRandomAccessStream());

                lock (imageLock)
                {
                    if (!ImageCache.ContainsKey(url))
                    {
                        // evict oldest when the bounded cache is full
                        if (imageOrder.Count >= MaxCachedImages)
                        {
                            var oldest = imageOrder[0];
                            imageOrder.RemoveAt(0);
                            ImageCache.Remove(oldest);
                        }
                        ImageCache[url] = image;
                        imageOrder.Add(url);
                    }
                }
                return image;
            }
            catch (HttpRequestException)
            {
                // try the next candidate result
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // decode failure on this candidate — try next
            }
        }

        return null;
    }

    internal static void InvalidatePreviewCache()
    {
        lock (imageLock)
        {
            ImageCache.Clear();
            imageOrder.Clear();
        }
    }

    private static async Task<string?> DownloadBackgroundAsync(
        ThemeCatalogEntry entry,
        string destinationFolder,
        CancellationToken cancellationToken)
    {
        string[] candidates;

        if (entry.BackgroundUrl is { Length: > 0 })
        {
            candidates = new[] { entry.BackgroundUrl };
        }
        else
        {
            // derive a filename from the theme name (e.g. "cotton candy" -> "cottoncandy")
            var themeNameSlug = new string(entry.Theme
                .Where(c => !char.IsWhiteSpace(c))
                .ToArray())
                .ToLowerInvariant();

            candidates = new[]
            {
                // theme-name-based (most common in user repos)
                GetSiblingUrl(entry.Url, themeNameSlug + ".png"),
                GetSiblingUrl(entry.Url, themeNameSlug + ".jpg"),
                GetSiblingUrl(entry.Url, themeNameSlug + ".jpeg"),
                // generic names
                GetSiblingUrl(entry.Url, "background.png"),
                GetSiblingUrl(entry.Url, "background.jpg"),
                GetSiblingUrl(entry.Url, "background.jpeg"),
                // wallpaper is another common convention
                GetSiblingUrl(entry.Url, "wallpaper.png"),
                GetSiblingUrl(entry.Url, "wallpaper.jpg"),
            };
        }

        foreach (var url in candidates)
        {
            try
            {
                var bytes = await HttpClient.GetByteArrayAsync(url, cancellationToken);
                var extension = Path.GetExtension(new Uri(url).AbsolutePath);
                if (extension is not ".png" and not ".jpg" and not ".jpeg")
                    extension = ".png";

                var path = Path.Combine(destinationFolder, "background" + extension);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                return path;
            }
            catch (HttpRequestException)
            {
                // try next candidate
            }
        }

        return null;
    }

    private static string GetSiblingUrl(string themeUrl, string fileName)
    {
        var uri = new Uri(themeUrl, UriKind.Absolute);
        return new Uri(uri, fileName).ToString();
    }

    public static string GetSafeDirectoryName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(name.Trim().Select(character =>
            Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character));
        return string.IsNullOrWhiteSpace(safeName) ? "unnamed-theme" : safeName;
    }
}