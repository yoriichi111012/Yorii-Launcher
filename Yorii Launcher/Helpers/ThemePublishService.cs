using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Yorii_Launcher.Models;

namespace Yorii_Launcher.Helpers;

public sealed class ThemePublishService
{
    // shared pooled connection instead of a bare client (no timeout = 100s hang)
    private static readonly HttpClient HttpClient = HttpService.Client;

    private const string RepoOwner = "yoriichi111012";
    private const string RepoName = "yorii-themes";
    private const string CatalogPath = "themes.yaml";
    private const string ThemesBranch = "main";

    public static bool IsLoggedIn => !string.IsNullOrEmpty(SettingsManager.Current.GitHubToken)
                                  && !string.IsNullOrEmpty(SettingsManager.Current.GitHubUsername);

    public static string CurrentUsername => SettingsManager.Current.GitHubUsername ?? "";

    private HttpRequestMessage CreateApiRequest(HttpMethod method, string apiPath)
    {
        var req = new HttpRequestMessage(method, $"https://api.github.com{apiPath}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SettingsManager.Current.GitHubToken);
        req.Headers.Add("Accept", "application/vnd.github+json");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("YoriiLauncher", "1.0"));
        return req;
    }

    public async Task EnsureRepoAccessAsync(CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
            throw new Exception("Not logged into GitHub.");

        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SettingsManager.Current.GitHubToken);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("YoriiLauncher", "1.0"));

        var resp = await HttpClient.SendAsync(req, cancellationToken);
        var scopes = resp.Headers.Contains("X-OAuth-Scopes")
            ? string.Join(",", resp.Headers.GetValues("X-OAuth-Scopes"))
            : "";

        if (!scopes.Contains("public_repo"))
            throw new Exception(
                "Your GitHub token doesn't have permission to publish themes. " +
                "Please sign out and sign back in to grant the required access.");
    }

    public async Task PublishThemeAsync(
        string themeName,
        ThemeDefinition definition,
        byte[]? previewImage,
        CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
            throw new Exception("Not logged into GitHub.");

        var item = DownloadManager.Add($"Publish · {themeName}", DownloadKind.Theme);

        try
        {
            await EnsureRepoAccessAsync(cancellationToken);

            var username = CurrentUsername;
            var safeName = GetSafeDirectoryName(themeName);

            item.SetIndeterminate();
            var themeYaml = LauncherYaml.Serialize(definition);
            await UploadFileAsync(safeName, "theme.yaml", themeYaml, $"Add theme: {themeName}", cancellationToken);

            if (previewImage is not null)
            {
                var ext = GetImageExtension(previewImage);
                await UploadFileBytesAsync(safeName, $"preview.{ext}", previewImage,
                    $"Add preview for: {themeName}", cancellationToken);
            }

            var bgBytes = TryReadBackgroundImage();
            if (bgBytes is not null)
            {
                var ext = GetImageExtension(bgBytes);
                await UploadFileBytesAsync(safeName, $"background.{ext}", bgBytes,
                    $"Add background for: {themeName}", cancellationToken);
            }

            var rawUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{ThemesBranch}/{Uri.EscapeDataString(safeName)}/theme.yaml";
            await UpdateCatalogAsync(themeName, username, rawUrl,
                $"Update catalog for: {themeName}", cancellationToken);

            // optimistic: reflect the new theme locally without waiting for cdn
            ThemeMarketplaceService.ApplyLocalCatalogUpdate(new ThemeCatalogEntry
            {
                Theme = themeName,
                Author = username,
                Url = rawUrl
            });
            item.Complete();
        }
        catch (Exception ex)
        {
            item.Fail(ex.Message);
            throw;
        }
    }

    public async Task DeleteThemeAsync(
        string themeName,
        CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
            throw new Exception("Not logged into GitHub.");

        await EnsureRepoAccessAsync(cancellationToken);

        var safeName = GetSafeDirectoryName(themeName);

        await DeleteFileAsync($"{safeName}/theme.yaml", $"Delete theme: {themeName}", cancellationToken);

        // remaining asset deletes in parallel (bounded) instead of 6 serial round-trips
        var assetPaths = new[] { "png", "jpg", "jpeg" }
            .SelectMany(ext => new[]
            {
                $"{safeName}/preview.{ext}",
                $"{safeName}/background.{ext}"
            })
            .ToList();

        using var gate = new SemaphoreSlim(3);
        await Task.WhenAll(assetPaths.Select(async path =>
        {
            await gate.WaitAsync(cancellationToken);
            try { await DeleteFileAsync(path, $"Cleanup: {themeName}", cancellationToken); }
            catch { }
            finally { gate.Release(); }
        }));

        await RemoveFromCatalogAsync(themeName, $"Remove from catalog: {themeName}", cancellationToken);
        ThemeMarketplaceService.ApplyLocalCatalogRemoval(themeName);
    }

    private async Task UploadFileAsync(string folder, string fileName, string content, string commitMessage, CancellationToken ct)
    {
        var path = $"{folder}/{fileName}";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        await PutFileAsync(path, base64, commitMessage, ct);
    }

    private async Task UploadFileBytesAsync(string folder, string fileName, byte[] data, string commitMessage, CancellationToken ct)
    {
        var path = $"{folder}/{fileName}";
        var base64 = Convert.ToBase64String(data);
        await PutFileAsync(path, base64, commitMessage, ct);
    }

    private static string EncodeApiPath(string path)
    {
        return string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }

    private async Task PutFileAsync(string path, string base64Content, string commitMessage, CancellationToken ct)
    {
        string? sha = null;
        try
        {
            sha = await GetFileShaAsync(path, ct);
        }
        catch { }

        var payload = new
        {
            message = commitMessage,
            content = base64Content,
            branch = ThemesBranch,
            sha
        };

        var req = CreateApiRequest(HttpMethod.Put, $"/repos/{RepoOwner}/{RepoName}/contents/{EncodeApiPath(path)}");
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

        var resp = await HttpClient.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Failed to upload {path}: {(int)resp.StatusCode} {body}");
    }

    private async Task DeleteFileAsync(string path, string commitMessage, CancellationToken ct)
    {
        var sha = await GetFileShaAsync(path, ct);
        if (sha is null)
            return;

        var payload = new
        {
            message = commitMessage,
            sha,
            branch = ThemesBranch
        };

        var req = CreateApiRequest(HttpMethod.Delete, $"/repos/{RepoOwner}/{RepoName}/contents/{EncodeApiPath(path)}");
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

        var resp = await HttpClient.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Failed to delete {path}: {(int)resp.StatusCode} {body}");
    }

    private async Task<string?> GetFileShaAsync(string path, CancellationToken ct)
    {
        var req = CreateApiRequest(HttpMethod.Get, $"/repos/{RepoOwner}/{RepoName}/contents/{EncodeApiPath(path)}?ref={ThemesBranch}");
        var resp = await HttpClient.SendAsync(req, ct);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("sha", out var shaEl))
            return shaEl.GetString();

        return null;
    }

    private async Task UpdateCatalogAsync(string themeName, string author, string themeUrl, string commitMessage, CancellationToken ct)
    {
        string catalogYaml;
        var sha = await GetFileShaAsync(CatalogPath, ct);

        if (sha is not null)
        {
            var req = CreateApiRequest(HttpMethod.Get, $"/repos/{RepoOwner}/{RepoName}/contents/{CatalogPath}?ref={ThemesBranch}");
            var resp = await HttpClient.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Failed to read catalog: {(int)resp.StatusCode} {body}");

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("content", out var contentEl))
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(contentEl.GetString()!));
                catalogYaml = UpdateCatalogYaml(decoded, themeName, author, themeUrl);
            }
            else
            {
                catalogYaml = CreateCatalogYaml(themeName, author, themeUrl);
            }
        }
        else
        {
            catalogYaml = CreateCatalogYaml(themeName, author, themeUrl);
            sha = null;
        }

        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(catalogYaml));
        var payload = new
        {
            message = commitMessage,
            content = base64,
            branch = ThemesBranch,
            sha
        };

        var putReq = CreateApiRequest(HttpMethod.Put, $"/repos/{RepoOwner}/{RepoName}/contents/{CatalogPath}");
        putReq.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

        var putResp = await HttpClient.SendAsync(putReq, ct);
        var putBody = await putResp.Content.ReadAsStringAsync(ct);

        if (!putResp.IsSuccessStatusCode)
            throw new Exception($"Failed to update catalog: {(int)putResp.StatusCode} {putBody}");
    }

    private async Task RemoveFromCatalogAsync(string themeName, string commitMessage, CancellationToken ct)
    {
        var sha = await GetFileShaAsync(CatalogPath, ct);
        if (sha is null)
            return;

        var req = CreateApiRequest(HttpMethod.Get, $"/repos/{RepoOwner}/{RepoName}/contents/{CatalogPath}?ref={ThemesBranch}");
        var resp = await HttpClient.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return;

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("content", out var contentEl))
            return;

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(contentEl.GetString()!));
        var updated = RemoveFromCatalogYaml(decoded, themeName);

        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(updated));
        var payload = new
        {
            message = commitMessage,
            content = base64,
            branch = ThemesBranch,
            sha
        };

        var putReq = CreateApiRequest(HttpMethod.Put, $"/repos/{RepoOwner}/{RepoName}/contents/{CatalogPath}");
        putReq.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

        var putResp = await HttpClient.SendAsync(putReq, ct);
        var putBody = await putResp.Content.ReadAsStringAsync(ct);

        if (!putResp.IsSuccessStatusCode)
            throw new Exception($"Failed to update catalog: {(int)putResp.StatusCode} {putBody}");
    }

    private static string CreateCatalogYaml(string themeName, string author, string themeUrl)
    {
        return $"themes:\n  - theme: \"{themeName}\"\n    author: \"{author}\"\n    url: \"{themeUrl}\"\n";
    }

    // yaml-safe catalog edit: parse → modify → serialize instead of fragile
    // line matching that corrupts entries when names overlap or quoting varies
    private static string UpdateCatalogYaml(string existing, string themeName, string author, string url)
    {
        var catalog = ParseCatalog(existing);
        var entry = catalog.FirstOrDefault(e => string.Equals(e.Theme, themeName, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            entry.Author = author;
            entry.Url = url;
        }
        else
        {
            catalog.Add(new ThemeCatalogEntry { Theme = themeName, Author = author, Url = url });
        }
        return SerializeCatalog(catalog);
    }

    private static string RemoveFromCatalogYaml(string existing, string themeName)
    {
        var catalog = ParseCatalog(existing);
        catalog.RemoveAll(e => string.Equals(e.Theme, themeName, StringComparison.OrdinalIgnoreCase));
        return SerializeCatalog(catalog);
    }

    private static List<ThemeCatalogEntry> ParseCatalog(string yaml)
    {
        try
        {
            var catalog = LauncherYaml.Deserialize<ThemeCatalog>(yaml);
            if (catalog?.Themes is { Count: > 0 })
                return catalog.Themes.ToList();

            var flat = LauncherYaml.Deserialize<List<ThemeCatalogEntry>>(yaml);
            if (flat is { Count: > 0 })
                return flat;
        }
        catch
        {
        }
        return [];
    }

    private static string SerializeCatalog(List<ThemeCatalogEntry> catalog) =>
        catalog.Count == 0
            ? "themes: []\n"
            : LauncherYaml.Serialize(new ThemeCatalog { Themes = catalog });

    public static ThemeDefinition ExtractCurrentTheme()
    {
        var settings = ThemeManager.Current;
        var userSettings = SettingsManager.Current;
        return new ThemeDefinition
        {
            ThemeMode = settings.CurrentTheme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true
                ? "light" : "dark",
            AccentColor = settings.UseCustomAccentColor ? settings.CustomAccentColor : null,
            OverlayEnabled = settings.OverlayOpacity > 0,
            OverlayOpacity = Math.Round(settings.OverlayOpacity, 2),
            OverlayBlur = settings.OverlayBlurEnabled,
            ServerlistEnabled = settings.ServerlistEnabled ?? userSettings.ServerListEnabled,
            WorldlistEnabled = settings.WorldlistEnabled ?? userSettings.WorldListEnabled,
            ReleasenotesEnabled = settings.ReleasenotesEnabled ?? userSettings.ShowReleaseNotesOnHome,
            CardBorderThickness = settings.CardBorderThickness,
            CardBorderColor = settings.CardBorderColor,
            CardBackgroundColor = settings.CardBackgroundColor,
            SettingscardBackgroundColor = settings.SettingscardBackgroundColor,
            SettingsexpanderHoverColor = settings.SettingsexpanderHoverColor,
            SettingsexpanderPressedColor = settings.SettingsexpanderPressedColor,
            SettingscardDisabledColor = settings.SettingscardDisabledColor,
            Systembackdrop = settings.Systembackdrop
        };
    }

    public static byte[]? TryReadBackgroundImage()
    {
        var bgPath = ThemeManager.Current.BackgroundImagePath;
        if (string.IsNullOrEmpty(bgPath) || !File.Exists(bgPath))
            return null;

        try
        {
            return File.ReadAllBytes(bgPath);
        }
        catch
        {
            return null;
        }
    }

    public static string? GetBackgroundImagePath()
    {
        var bgPath = ThemeManager.Current.BackgroundImagePath;
        if (string.IsNullOrEmpty(bgPath) || !File.Exists(bgPath))
            return null;
        return bgPath;
    }

    private static string GetSafeDirectoryName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(name.Trim().Select(c =>
            Array.IndexOf(invalidCharacters, c) >= 0 ? '-' : c));
        return string.IsNullOrWhiteSpace(safeName) ? "unnamed-theme" : safeName.ToLowerInvariant();
    }

    private static string GetImageExtension(byte[] data)
    {
        if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
            return "jpg";
        return "png";
    }
}