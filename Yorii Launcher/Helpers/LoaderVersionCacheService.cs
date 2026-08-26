using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    // yorii skins is our cloudflare auth server worker which fetches skins from github repo
    public static class LoaderVersionCacheService
    {
        private const string CacheFolderName = "LoaderVersionCache";
        private const string CacheFileName = "loaderVersions.json";

        public const string IndexRepoOwner = "yorii-accounts";
        public const string IndexRepo = "mc-version-index";
        private const string IndexFile = "mc-version-index.json";

        private static readonly string BundledIndexPath =
            Path.Combine(AppContext.BaseDirectory, IndexFile);

        // how old index can be before background refresh
        private static readonly TimeSpan FreshWindow = TimeSpan.FromHours(24);

        private const string RemoteIndexUrl =
            $"https://raw.githubusercontent.com/{IndexRepoOwner}/{IndexRepo}/main/{IndexFile}";

        // bundled seed ships with launcher so list is never empty even offline on first run
        public static LoaderVersionCache? LoadBundled()
        {
            try
            {
                if (!File.Exists(BundledIndexPath))
                    return null;
                return Deserialize(File.ReadAllText(BundledIndexPath));
            }
            catch
            {
                return null;
            }
        }

        public static async Task<LoaderVersionCache?> LoadAsync()
        {
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    CacheFolderName, CreationCollisionOption.OpenIfExists);
                var file = await folder.GetFileAsync(CacheFileName);
                var json = await FileIO.ReadTextAsync(file);
                return Deserialize(json);
            }
            catch
            {
                return null;
            }
        }

        public static async Task SaveAsync(LoaderVersionCache cache)
        {
            try
            {
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    CacheFolderName, CreationCollisionOption.OpenIfExists);
                var file = await folder.CreateFileAsync(CacheFileName, CreationCollisionOption.ReplaceExisting);
                var json = Serialize(cache);
                await FileIO.WriteTextAsync(file, json);
            }
            catch
            {
                // dont break version loading if cache write fails
            }
        }

        public static bool IsFresh(LoaderVersionCache? cache) =>
            cache != null &&
            cache.CachedAt + FreshWindow > DateTimeOffset.UtcNow;

        // grab shared index from github - one request fast
        public static async Task<LoaderVersionCache?> FetchRemoteAsync()
        {
            try
            {
                using var web = new HttpClient();
                var json = await web.GetStringAsync(RemoteIndexUrl);
                return Deserialize(json);
            }
            catch
            {
                return null;
            }
        }

        // push fresh index to shared repo with yorii skins token, skip if not logged in - only one launcher needs to push
        public static async Task PushRemoteAsync(LoaderVersionCache cache)
        {
            string token = SettingsManager.Current.GitHubToken ?? "";
            if (string.IsNullOrEmpty(token))
                return;

            try
            {
                var json = Serialize(cache);
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                const string apiBase = "https://api.github.com";
                const string branch = "main";

                using var web = new HttpClient();

                // get current blob sha so we update instead of creating duplicate
                string? sha = null;
                var getReq = new HttpRequestMessage(HttpMethod.Get, $"{apiBase}/repos/{IndexRepoOwner}/{IndexRepo}/contents/{IndexFile}");
                getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                getReq.Headers.Add("Accept", "application/vnd.github+json");
                getReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("YoriiLauncher", "1.0"));
                var getResp = await web.SendAsync(getReq);
                if (getResp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("sha", out var shaEl))
                        sha = shaEl.GetString();
                }

                var payload = new
                {
                    message = $"Update version index ({DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC)",
                    content = base64,
                    branch,
                    sha
                };

                var putReq = new HttpRequestMessage(HttpMethod.Put, $"{apiBase}/repos/{IndexRepoOwner}/{IndexRepo}/contents/{IndexFile}");
                putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                putReq.Headers.Add("Accept", "application/vnd.github+json");
                putReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("YoriiLauncher", "1.0"));
                putReq.Content = new StringContent(
                    JsonSerializer.Serialize(payload), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

                await web.SendAsync(putReq);
            }
            catch
            {
                // push fail just means other launchers refresh later
            }
        }

        // merge index sources, later wins on name conflicts so probed beats seed, newest timestamp wins
        public static LoaderVersionCache Merge(params LoaderVersionCache?[] sources)
        {
            var entries = new System.Collections.Generic.Dictionary<string, VersionIndexEntry>(StringComparer.Ordinal);
            DateTimeOffset cachedAt = DateTimeOffset.MinValue;

            foreach (var source in sources)
            {
                if (source == null) continue;
                if (source.CachedAt > cachedAt) cachedAt = source.CachedAt;
                foreach (var entry in source.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    entries[entry.Name] = entry;
                }
            }

            return new LoaderVersionCache
            {
                Entries = entries.Values.ToList(),
                // keep minvalue if no timestamp so empty index stays stale and still probes
                CachedAt = cachedAt == DateTimeOffset.MinValue ? DateTimeOffset.MinValue : cachedAt
            };
        }

        private static LoaderVersionCache? Deserialize(string json) =>
            System.Text.Json.JsonSerializer.Deserialize(
                json, LauncherJsonContext.Default.LoaderVersionCache);

        private static string Serialize(LoaderVersionCache cache) =>
            System.Text.Json.JsonSerializer.Serialize(
                cache, LauncherJsonContext.Default.LoaderVersionCache);
    }
}