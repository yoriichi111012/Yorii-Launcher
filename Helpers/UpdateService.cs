using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;

namespace Yorii_Launcher.Helpers
{
    public static class UpdateService
    {
        private const string RepoOwner = "yoriichi111012";
        private const string RepoName = "Yorii-Launcher";
        private const string ReleasesApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        private static readonly HttpClient ApiClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestHeaders =
            {
                { "User-Agent", "YoriiLauncher" },
                { "Accept", "application/vnd.github+json" }
            }
        };

        // separate client for downloads with a long timeout
        private static readonly HttpClient DownloadClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        public static UpdateInfo? LastCheckedUpdate { get; private set; }

        public class UpdateInfo
        {
            public Version Version { get; init; } = new();
            public string? DownloadUrl { get; init; }
            public string? AssetName { get; init; }
            public string? ReleaseNotes { get; init; }
        }

        public static Version GetCurrentVersion()
        {
            try
            {
                var ver = Package.Current.Id.Version;
                return new Version(ver.Major, ver.Minor, ver.Build, ver.Revision);
            }
            catch
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (asm != null)
                    return new Version(asm.Major, asm.Minor, asm.Build, asm.Revision);
                return new Version(0, 0, 0, 0);
            }
        }

        public static async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            try
            {
                Debug.WriteLine($"[UpdateService] Checking for updates from {ReleasesApiUrl}");
                var response = await ApiClient.GetAsync(ReleasesApiUrl);
                Debug.WriteLine($"[UpdateService] API response: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[UpdateService] API returned {response.StatusCode}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[UpdateService] Release JSON (first 200 chars): {json[..Math.Min(200, json.Length)]}");
                var release = JsonSerializer.Deserialize<GitHubRelease>(json);
                if (release == null || string.IsNullOrEmpty(release.TagName))
                {
                    Debug.WriteLine("[UpdateService] Failed to parse release or tag_name is empty");
                    return null;
                }

                Debug.WriteLine($"[UpdateService] Latest tag: {release.TagName}");
                var latestVersion = ParseVersion(release.TagName);
                if (latestVersion == null)
                {
                    Debug.WriteLine($"[UpdateService] Failed to parse version from tag: {release.TagName}");
                    return null;
                }

                var currentVersion = GetCurrentVersion();
                Debug.WriteLine($"[UpdateService] Current: {currentVersion}, Latest: {latestVersion}");

                if (latestVersion <= currentVersion)
                {
                    Debug.WriteLine("[UpdateService] Already up to date");
                    return null;
                }

                Debug.WriteLine("[UpdateService] Update available!");
                var arch = GetCurrentArchitecture();
                Debug.WriteLine($"[UpdateService] Architecture: {arch}");
                var msixAsset = release.Assets?
                    .FirstOrDefault(a => a.Name != null
                        && a.Name.StartsWith("Yorii.Launcher_", StringComparison.OrdinalIgnoreCase)
                        && a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase)
                        && a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase));

                Debug.WriteLine($"[UpdateService] MSIX asset: {msixAsset?.Name ?? "NOT FOUND"}");
                if (msixAsset?.BrowserDownloadUrl == null)
                    return null;

                Debug.WriteLine($"[UpdateService] Download URL: {msixAsset.BrowserDownloadUrl}");
                var info = new UpdateInfo
                {
                    Version = latestVersion,
                    DownloadUrl = msixAsset.BrowserDownloadUrl,
                    AssetName = msixAsset.Name,
                    ReleaseNotes = release.Body
                };
                LastCheckedUpdate = info;
                return info;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Check failed: {ex}");
                return null;
            }
        }

        public static async Task<string?> DownloadUpdateAsync(UpdateInfo info, IProgress<double>? progress = null)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "YoriiLauncher_Update");
                Directory.CreateDirectory(tempDir);

                var msixPath = Path.Combine(tempDir, info.AssetName ?? "update.msix");

                using var response = await DownloadClient.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(msixPath);

                var buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloaded += bytesRead;

                    if (totalBytes > 0)
                        progress?.Report((double)downloaded / totalBytes * 100);
                }

                progress?.Report(100);
                return msixPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Download failed: {ex.Message}");
                return null;
            }
        }

        public static void LaunchMsix(string msixPath)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = msixPath,
                UseShellExecute = true
            });
        }

        private static string GetCurrentArchitecture()
        {
            var arch = RuntimeInformation.ProcessArchitecture;
            return arch switch
            {
                Architecture.X86 => "x86",
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => "x64"
            };
        }

        // tags use preview-0.7 format, strip prefix to get version
        private static Version? ParseVersion(string tagName)
        {
            var versionString = tagName.TrimStart('v', 'V');
            var dashIndex = versionString.IndexOf('-');
            if (dashIndex >= 0)
                versionString = versionString[(dashIndex + 1)..];

            if (!Version.TryParse(versionString, out var version))
                return null;

            int major = version.Major;
            int minor = version.Minor;
            int build = version.Build < 0 ? 0 : version.Build;
            int revision = version.Revision < 0 ? 0 : version.Revision;
            return new Version(major, minor, build, revision);
        }

        private class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("assets")]
            public GitHubAsset[]? Assets { get; set; }
        }

        private class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}
