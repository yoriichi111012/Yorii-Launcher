using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    // grabs release notes from mojang and caches them locally, this was pretty hard to get working
    public sealed class MinecraftReleaseNotesService
    {
        private const string BaseUrl = "https://launchercontent.mojang.com/v2/";
        private const string PatchNotesUrl = BaseUrl + "javaPatchNotes.json";
        private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        private const string CacheFolderName = "ReleaseNotesCache";
        private const string PatchNotesCacheFile = "patchNotes.json";
        private const string ManifestCacheFile = "versionManifest.json";
        private readonly HttpClient httpClient;

        // show local cache instantly and refresh in background, mojang endpoints are slow so waiting every home visit feels laggy
        private static readonly TimeSpan IndexTtl = TimeSpan.FromHours(1);
        private static List<MinecraftReleaseNote>? _patchMemo;
        private static DateTime _patchFetchedAt;
        private static List<MinecraftVersionManifestItem>? _manifestMemo;
        private static DateTime _manifestFetchedAt;

        public MinecraftReleaseNotesService(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        public async Task<List<MinecraftReleaseNote>> GetReleaseNotesAsync()
        {
            var patchNoteEntries = await GetPatchNoteEntriesAsync();
            // group by version keep first per version
            var notesByVersion = patchNoteEntries
                .GroupBy(entry => entry.Version, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var manifestVersions = await GetManifestVersionsAsync();

            if (manifestVersions.Count == 0)
                return patchNoteEntries;

            var result = new List<MinecraftReleaseNote>();

            // match manifest to patch notes add placeholder if missing
            foreach (var manifestVersion in manifestVersions)
            {
                if (notesByVersion.TryGetValue(manifestVersion.Id, out var note))
                {
                    result.Add(note);
                    continue;
                }

                result.Add(new MinecraftReleaseNote
                {
                    Title = PrettifyTitle(manifestVersion.Id),
                    Version = manifestVersion.Id,
                    Type = manifestVersion.Type,
                    Date = manifestVersion.ReleaseTime,
                    ShortText = "No official release notes were published for this version."
                });
            }

            return result;
        }

        // prettify snapshot ids like 26.3-snapshot-10 to minecraft 26.3 snapshot 10, used when mojang shipped version before notes
        private static string PrettifyTitle(string id)
        {
            var match = System.Text.RegularExpressions.Regex.Match(id, @"^(\d+(?:\.\d+)*)-snapshot-(\d+)$");
            if (match.Success)
                return $"Minecraft {match.Groups[1].Value} Snapshot {match.Groups[2].Value}";

            return $"Minecraft {id}";
        }

        public async Task<bool> IsHtmlCached(MinecraftReleaseNote releaseNote)
        {
            if (string.IsNullOrWhiteSpace(releaseNote.ContentPath))
                return false;

            var cached = await GetCachedHtmlAsync(releaseNote.ContentPath);
            return cached != null;
        }

        public async Task<string> GetReleaseNoteHtmlAsync(MinecraftReleaseNote releaseNote)
        {
            if (!string.IsNullOrWhiteSpace(releaseNote.ContentPath))
            {
                // check cache first
                var cached = await GetCachedHtmlAsync(releaseNote.ContentPath);
                if (cached != null)
                    return cached;

                try
                {
                    // try fetch from mojang
                    var url = new Uri(new Uri(BaseUrl), releaseNote.ContentPath);
                    using var response = await httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    await using var stream = await response.Content.ReadAsStreamAsync();
                    var content = await JsonSerializer.DeserializeAsync(
                        stream,
                        LauncherJsonContext.Default.MinecraftReleaseNoteContent);

                    if (!string.IsNullOrWhiteSpace(content?.Body))
                    {
                        await CacheHtmlAsync(releaseNote.ContentPath, content.Body);
                        return content.Body;
                    }
                }
                catch
                {
                    // offline so try cache again
                    var fallback = await GetCachedHtmlAsync(releaseNote.ContentPath);
                    if (fallback != null)
                        return fallback;
                }
            }

            // build fallback html if nothing else
            var title = WebUtility.HtmlEncode(releaseNote.Title);
            var shortText = WebUtility.HtmlEncode(releaseNote.ShortText);
            var type = WebUtility.HtmlEncode(releaseNote.Type);

            if (string.IsNullOrWhiteSpace(shortText))
                shortText = "No official release notes were published for this version.";

            return $"<h1>{title}</h1><p><strong>{type}</strong></p><p>{shortText}</p>";
        }

        private static string SanitizeCacheKey(string contentPath)
        {
            return contentPath.Replace('/', '_').Replace('\\', '_');
        }

        private static async Task<string?> GetCachedHtmlAsync(string contentPath)
        {
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    CacheFolderName, CreationCollisionOption.OpenIfExists);

                var fileName = SanitizeCacheKey(contentPath) + ".html";
                var file = await folder.TryGetItemAsync(fileName) as StorageFile;
                if (file == null)
                    return null;

                return await FileIO.ReadTextAsync(file);
            }
            catch
            {
                return null;
            }
        }

        private static async Task CacheHtmlAsync(string contentPath, string html)
        {
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    CacheFolderName, CreationCollisionOption.OpenIfExists);

                var fileName = SanitizeCacheKey(contentPath) + ".html";
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, html);
            }
            catch
            {
            }
        }

        private async Task<List<MinecraftReleaseNote>> GetPatchNoteEntriesAsync()
        {
            // 1 fresh in memory
            if (_patchMemo is not null && DateTime.UtcNow - _patchFetchedAt < IndexTtl)
                return _patchMemo;

            // 2 disk cache instantly refresh from network in background
            var cached = await GetCachedPatchNotesAsync();
            if (cached.Count > 0)
            {
                _patchMemo = cached;
                _patchFetchedAt = DateTime.UtcNow;
                _ = RefreshPatchNotesInBackgroundAsync();
                return cached;
            }

            // 3 nothing cached so blocking fetch
            return await FetchPatchNotesFromWebAsync();
        }

        private async Task RefreshPatchNotesInBackgroundAsync()
        {
            try
            {
                var fresh = await FetchPatchNotesFromWebAsync();
                if (fresh.Count > 0)
                {
                    _patchMemo = fresh;
                    _patchFetchedAt = DateTime.UtcNow;
                }
            }
            catch
            {
                // offline keep serving cache
            }
        }

        private async Task<List<MinecraftReleaseNote>> FetchPatchNotesFromWebAsync()
        {
            try
            {
                using var response = await httpClient.GetAsync(PatchNotesUrl);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                await CacheRawAsync(PatchNotesCacheFile, json);

                var patchNotes = JsonSerializer.Deserialize(
                    json,
                    LauncherJsonContext.Default.MinecraftPatchNotesResponse);

                // drop entries without version newest first
                var entries = patchNotes?.Entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Version))
                    .OrderByDescending(entry => entry.Date)
                    .ToList() ?? [];

                if (entries.Count > 0)
                {
                    _patchMemo = entries;
                    _patchFetchedAt = DateTime.UtcNow;
                }
                return entries;
            }
            catch
            {
                return await GetCachedPatchNotesAsync();
            }
        }

        private async Task<List<MinecraftVersionManifestItem>> GetManifestVersionsAsync()
        {
            // same disk first pattern as patch notes
            if (_manifestMemo is not null && DateTime.UtcNow - _manifestFetchedAt < IndexTtl)
                return _manifestMemo;

            var cached = await GetCachedManifestAsync();
            if (cached.Count > 0)
            {
                _manifestMemo = cached;
                _manifestFetchedAt = DateTime.UtcNow;
                _ = RefreshManifestInBackgroundAsync();
                return cached;
            }

            return await FetchManifestFromWebAsync();
        }

        private async Task RefreshManifestInBackgroundAsync()
        {
            try
            {
                var fresh = await FetchManifestFromWebAsync();
                if (fresh.Count > 0)
                {
                    _manifestMemo = fresh;
                    _manifestFetchedAt = DateTime.UtcNow;
                }
            }
            catch
            {
            }
        }

        private async Task<List<MinecraftVersionManifestItem>> FetchManifestFromWebAsync()
        {
            try
            {
                using var response = await httpClient.GetAsync(ManifestUrl);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                await CacheRawAsync(ManifestCacheFile, json);

                var manifest = JsonSerializer.Deserialize(
                    json,
                    LauncherJsonContext.Default.MinecraftVersionManifestResponse);

                // only keep releases and snapshots
                var versions = manifest?.Versions
                    .Where(version =>
                        !string.IsNullOrWhiteSpace(version.Id) &&
                        (version.Type == "release" || version.Type == "snapshot"))
                    .ToList() ?? [];

                if (versions.Count > 0)
                {
                    _manifestMemo = versions;
                    _manifestFetchedAt = DateTime.UtcNow;
                }
                return versions;
            }
            catch
            {
                return await GetCachedManifestAsync();
            }
        }

        private static async Task<List<MinecraftReleaseNote>> GetCachedPatchNotesAsync()
        {
            var json = await GetCachedRawAsync(PatchNotesCacheFile);
            if (json == null) return [];

            var patchNotes = JsonSerializer.Deserialize(
                json,
                LauncherJsonContext.Default.MinecraftPatchNotesResponse);

            return patchNotes?.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Version))
                .OrderByDescending(entry => entry.Date)
                .ToList() ?? [];
        }

        private static async Task<List<MinecraftVersionManifestItem>> GetCachedManifestAsync()
        {
            var json = await GetCachedRawAsync(ManifestCacheFile);
            if (json == null) return [];

            var manifest = JsonSerializer.Deserialize(
                json,
                LauncherJsonContext.Default.MinecraftVersionManifestResponse);

            return manifest?.Versions
                .Where(version =>
                    !string.IsNullOrWhiteSpace(version.Id) &&
                    (version.Type == "release" || version.Type == "snapshot"))
                .ToList() ?? [];
        }

        private static async Task CacheRawAsync(string fileName, string json)
        {
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    CacheFolderName, CreationCollisionOption.OpenIfExists);

                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, json);
            }
            catch
            {
            }
        }

        private static async Task<string?> GetCachedRawAsync(string fileName)
        {
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    CacheFolderName, CreationCollisionOption.OpenIfExists);

                var file = await folder.TryGetItemAsync(fileName) as StorageFile;
                if (file == null) return null;

                return await FileIO.ReadTextAsync(file);
            }
            catch
            {
                return null;
            }
        }
    }
}