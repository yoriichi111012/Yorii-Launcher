using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    public static class SkinsLoaderUpdateService
    {
        private const string JarUrl =
            "https://raw.githubusercontent.com/yoriichi111012/Yorii-Launcher/main/Yorii%20Launcher/yoriiSkinsLoader.jar";
        private const string ExpectedModId = "yoriskinsloader";
        private const string CacheDirName = "SkinsLoader";
        private const string StateFileName = "state.json";
        private const string JarFileName = "yoriiSkinsLoader.jar";

        private static readonly TimeSpan FreshWindow = TimeSpan.FromHours(24);
        private static bool refreshRunning;

        private static string CacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Yorii Launcher",
            CacheDirName);

        private static string CachedJarPath() => Path.Combine(CacheDir, JarFileName);
        private static string StatePath() => Path.Combine(CacheDir, StateFileName);

        // newest jar on disk wins so a bundled update always beats a stale download
        public static string GetSourceJar()
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, InstanceManager.YoriiSkinsLoaderJar);
            string cached = CachedJarPath();
            if (!File.Exists(cached))
                return bundled;
            if (!File.Exists(bundled))
                return cached;
            return JarVersion(cached) >= JarVersion(bundled) ? cached : bundled;
        }

        public static void RefreshInBackground()
        {
            if (refreshRunning)
                return;
            refreshRunning = true;
            _ = Task.Run(async () =>
            {
                try { await RefreshAsync(); }
                catch { }
                finally { refreshRunning = false; }
            });
        }

        private static async Task RefreshAsync()
        {
            if (App.IsShuttingDown)
                return;

            var state = ReadState();
            if (state != null && state.CheckedAt + FreshWindow > DateTimeOffset.UtcNow)
                return;

            byte[]? bytes = null;
            string etag = state?.ETag ?? "";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, JarUrl);
                if (EntityTagHeaderValue.TryParse(state?.ETag, out var tag))
                    request.Headers.IfNoneMatch.Add(tag);
                using var response = await HttpService.Client.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    WriteState(state!.ETag, state.Version);
                    return;
                }
                response.EnsureSuccessStatusCode();
                bytes = await response.Content.ReadAsByteArrayAsync();
                etag = response.Headers.ETag?.ToString() ?? "";
            }
            catch
            {
                return;
            }

            if (bytes == null || !IsYoriiSkinsLoaderJar(bytes))
            {
                Logger.Warn("SkinsLoader download failed validation, keeping current jar");
                return;
            }

            try
            {
                var best = GetSourceJar();
                if (File.Exists(best) && bytes.SequenceEqual(File.ReadAllBytes(best)))
                {
                    WriteState(etag, JarVersionText(best));
                    return;
                }

                Directory.CreateDirectory(CacheDir);
                var tmp = CachedJarPath() + ".tmp";
                await File.WriteAllBytesAsync(tmp, bytes);
                File.Move(tmp, CachedJarPath(), true);
            }
            catch
            {
                return;
            }

            var version = JarVersionText(CachedJarPath());
            WriteState(etag, version);
            Logger.Info($"SkinsLoader updated to {version}");
            if (App.IsShuttingDown)
                return;
            // installing touches instance icons (xaml bitmaps), so it must
            // run on the ui thread even though the download ran in background
            var queue = MainWindow.Instance?.DispatcherQueue;
            if (queue is null)
                return;
            if (queue.HasThreadAccess)
                InstanceManager.EnsureYoriiSkinsLoaderInstalled();
            else
                queue.TryEnqueue(InstanceManager.EnsureYoriiSkinsLoaderInstalled);
        }

        private static bool IsYoriiSkinsLoaderJar(byte[] bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                var entry = zip.Entries.FirstOrDefault(e => e.FullName == "fabric.mod.json");
                if (entry == null)
                    return false;
                using var entryStream = entry.Open();
                using var doc = JsonDocument.Parse(entryStream);
                return doc.RootElement.TryGetProperty("id", out var id) &&
                    id.GetString() == ExpectedModId;
            }
            catch
            {
                return false;
            }
        }

        private static string JarVersionText(string path)
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.Entries.FirstOrDefault(e => e.FullName == "fabric.mod.json");
                if (entry == null)
                    return "";
                using var stream = entry.Open();
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("version", out var version))
                    return version.GetString() ?? "";
            }
            catch
            {
            }
            return "";
        }

        private static Version JarVersion(string path) =>
            Version.TryParse(JarVersionText(path), out var parsed) ? parsed : new Version(0, 0);

        private static SkinsLoaderState? ReadState()
        {
            try
            {
                var path = StatePath();
                if (!File.Exists(path))
                    return null;
                return JsonSerializer.Deserialize(
                    File.ReadAllText(path),
                    LauncherJsonContext.Default.SkinsLoaderState);
            }
            catch
            {
                return null;
            }
        }

        private static void WriteState(string etag, string version)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(
                    StatePath(),
                    JsonSerializer.Serialize(
                        new SkinsLoaderState { ETag = etag, CheckedAt = DateTimeOffset.UtcNow, Version = version },
                        LauncherJsonContext.Default.SkinsLoaderState));
            }
            catch
            {
            }
        }
    }
}
