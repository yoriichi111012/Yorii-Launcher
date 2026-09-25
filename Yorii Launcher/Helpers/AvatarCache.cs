using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Yorii_Launcher.Helpers;

// Disk-cached GitHub avatars so the account button keeps its picture
// offline instead of going blank every time the avatar URL fails to load.
// Refresh is best-effort in the background; any failure falls back to the
// last cached file. No reflection, trim/AOT-safe.
internal static class AvatarCache
{
    private static string CacheDir => Path.Combine(
        ApplicationData.Current.LocalFolder.Path, "AvatarCache");

    private static string PathFor(string username) =>
        Path.Combine(CacheDir, $"{SafeName(username)}.png");

    public static string? GetCachedPath(string username)
    {
        try
        {
            var path = PathFor(username);
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string?> EnsureFreshAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var cached = GetCachedPath(username);
        try
        {
            using var response = await HttpService.Client.GetAsync(
                new Uri($"https://avatars.githubusercontent.com/{username}?size=128"),
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            Directory.CreateDirectory(CacheDir);
            var path = PathFor(username);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return path;
        }
        catch
        {
            // offline or rate-limited: keep showing the last cached avatar
            return cached;
        }
    }

    private static string SafeName(string username)
    {
        var safe = new string(username.Trim().Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unnamed" : safe;
    }
}
