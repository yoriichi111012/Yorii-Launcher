using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers
{
    public static class UpdateService
    {
        private const string RepoOwner = "yoriichi111012";
        private const string RepoName = "Yorii-Launcher";
        private const string DownloadBaseUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/latest/download";

        public static UpdateInfo? LastCheckedUpdate { get; private set; }

        public class UpdateInfo
        {
            public Version Version { get; init; } = new();
            public string? DownloadUrl { get; init; }
            public string? AssetName { get; init; }
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

        // switched from github api to a direct head request — no rate limits
        // no json parsing, same pattern as the installer script
        public static async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            try
        {
                var arch = GetCurrentArchitecture();
                var packageUrl = $"{DownloadBaseUrl}/Yorii.Launcher_{arch}.msix";

                Logger.Info($"Checking update: HEAD {packageUrl}");

                using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Add("User-Agent", "Yorii_Launcher");

                using var request = new HttpRequestMessage(HttpMethod.Head, packageUrl);
                using var response = await client.SendAsync(request);

                if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400)
                {
                    Logger.Warn($"Expected redirect, got {response.StatusCode}");
                    return null;
                }

                var location = response.Headers.Location;
                if (location == null)
                {
                    Logger.Warn("No Location header in redirect");
                    return null;
                }

        // location may be relative or absolute, either way resolve against github.com
                var redirectUri = location.IsAbsoluteUri ? location : new Uri(new Uri("https://github.com"), location);

                Logger.Info($"Redirect → {redirectUri}");

                // path segments: /, user/, repo/, releases/, download/, v0.7/, file.msix
                var segments = redirectUri.Segments;
                if (segments.Length < 7)
                {
                    Logger.Warn("Unexpected redirect path structure");
                    return null;
                }

                var tagName = segments[^2].TrimEnd('/');
                var assetName = segments[^1];

                var latestVersion = ParseVersion(tagName);
                if (latestVersion == null)
                {
                    Logger.Warn($"Failed to parse version from tag: {tagName}");
                    return null;
                }

                var currentVersion = GetCurrentVersion();
                Logger.Info($"Current: {currentVersion}, Latest: {latestVersion}");

                if (latestVersion <= currentVersion)
                {
                    Logger.Info("Already up to date");
                    return null;
                }

                Logger.Info("Update available!");
                var info = new UpdateInfo
                {
                    Version = latestVersion,
                    DownloadUrl = redirectUri.ToString(),
                    AssetName = assetName
                };
                LastCheckedUpdate = info;
                return info;
            }
            catch (Exception ex)
            {
                Logger.Error($"Update check failed: {ex.Message}");
                return null;
            }
        }

        public static async Task<string?> DownloadUpdateAsync(UpdateInfo info, IProgress<double>? progress = null, DownloadItem? item = null)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "Quiescent_Update");
                Directory.CreateDirectory(tempDir);

                var msixPath = Path.Combine(tempDir, info.AssetName ?? "update.msix");

                item ??= DownloadManager.Add($"Yorii Launcher update {info.Version}", DownloadKind.Update);

                using var response = await HttpService.DownloadClient.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, item.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using var contentStream = await response.Content.ReadAsStreamAsync(item.Token).ConfigureAwait(false);
                await using var fileStream = File.Create(msixPath, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);

                var buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, item.Token).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), item.Token).ConfigureAwait(false);
                    downloaded += bytesRead;

                    if (totalBytes > 0)
                    {
                        progress?.Report((double)downloaded / totalBytes * 100);
                        item.SetByteProgress(downloaded, totalBytes);
                    }
                }

                item.Complete();
                progress?.Report(100);
                return msixPath;
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Update download cancelled by user");
                item?.Cancel();
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Download failed: {ex.Message}");
                item?.Fail(ex.Message);
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

        // tags use preview-0.7 or v0.7 format
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
    }
}
