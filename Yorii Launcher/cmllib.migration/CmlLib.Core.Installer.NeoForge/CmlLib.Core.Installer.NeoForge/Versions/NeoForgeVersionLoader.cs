using CmlLib.Core.Installer.NeoForge.Models;
using HtmlAgilityPack;

namespace CmlLib.Core.Installer.NeoForge.Versions;

public class NeoForgeVersionLoader
{
    private readonly HttpClient _httpClient;

    private const string _forgeVersionManifest =
        "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";

    public NeoForgeVersionLoader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Converts a Minecraft version string to the NeoForge manifest prefix used for filtering.
    /// Handles both legacy "1.x.y" format and new "YY.x" format (e.g. "26.1").
    /// </summary>
    private static string GetManifestPrefix(string mcVersion)
    {
        // Legacy format: "1.20.2", "1.21.4" etc. — NeoForge drops the leading "1."
        // e.g. "1.21.4" → "21.4"
        if (mcVersion.StartsWith("1."))
            return mcVersion.Substring(2);

        // New format: "26.1", "26.2" etc. — used as-is
        return mcVersion;
    }

    /// <summary>
    /// Returns true if the NeoForge version string is a stable or beta release.
    /// Filters out alpha/snapshot builds that contain SemVer build metadata ("+").
    /// </summary>
    private static bool IsStableOrBeta(string version)
    {
        // Build metadata separator in SemVer — e.g. "26.1.0.0-alpha.1+snapshot-1"
        // These are pre-alpha snapshots not suitable for general use.
        return !version.Contains('+');
    }

    /// <summary>
    /// Returns all available NeoForge versions for the given Minecraft version,
    /// ordered from newest to oldest. Snapshots/alphas with build metadata are excluded.
    /// </summary>
    public async Task<IEnumerable<NeoForgeVersion>> GetNeoForgeVersions(string mcVersion)
    {
        var prefix = GetManifestPrefix(mcVersion);

        // Append "." so that e.g. "21.1" does not accidentally match "21.10.x"
        var filterPrefix = prefix + ".";

        var stream = await _httpClient.GetStreamAsync(_forgeVersionManifest);
        var manifest = await System.Text.Json.JsonSerializer.DeserializeAsync<NeoForgeManifest>(stream);

        if (manifest == null)
            return Array.Empty<NeoForgeVersion>();

        var matchingVersions = manifest.Versions
            .Where(v => v.StartsWith(filterPrefix) && IsStableOrBeta(v))
            .Select(v => new NeoForgeVersion(mcVersion, v));

        // Manifest is oldest-first; reverse so callers get newest first,
        // consistent with the Install() method using FirstOrDefault() for "latest".
        return matchingVersions.Reverse();
    }

    /// <summary>
    /// Returns all available NeoForge versions including pre-release alphas and snapshots.
    /// Use this only when you explicitly want to expose snapshot builds.
    /// </summary>
    public async Task<IEnumerable<NeoForgeVersion>> GetAllNeoForgeVersions(string mcVersion)
    {
        var prefix = GetManifestPrefix(mcVersion);
        var filterPrefix = prefix + ".";

        var stream = await _httpClient.GetStreamAsync(_forgeVersionManifest);
        var manifest = await System.Text.Json.JsonSerializer.DeserializeAsync<NeoForgeManifest>(stream);

        if (manifest == null)
            return Array.Empty<NeoForgeVersion>();

        return manifest.Versions
            .Where(v => v.StartsWith(filterPrefix))
            .Select(v => new NeoForgeVersion(mcVersion, v))
            .Reverse();
    }

    private IEnumerable<NeoForgeVersionFile> getForgeVersionFiles(HtmlNode node)
    {
        var lis = node.SelectNodes("ul[1]/li");
        if (lis == null)
            return Enumerable.Empty<NeoForgeVersionFile>();

        var files = new List<NeoForgeVersionFile>();
        foreach (var li in lis)
        {
            var forgeVersionFile = new NeoForgeVersionFile();
            string? firstLink = null, secondLink = null;

            var firstANode = li.SelectSingleNode("a[1]");
            if (firstANode != null)
            {
                firstLink = firstANode.GetAttributeValue("href", "").Trim();
                forgeVersionFile.Type = firstANode.InnerText.Trim();
            }

            var infoTooltip = li.Descendants().FirstOrDefault(node => node.HasClass("info-tooltip"));
            if (infoTooltip != default)
            {
                forgeVersionFile.MD5 = infoTooltip.ChildNodes[2].InnerText.Trim();
                forgeVersionFile.SHA1 = infoTooltip.ChildNodes[6].InnerText.Trim();
                secondLink = infoTooltip
                    .Descendants("a")
                    .FirstOrDefault()?
                    .GetAttributeValue("href", "")?
                    .Trim();
            }

            if (string.IsNullOrEmpty(secondLink))
                forgeVersionFile.DirectUrl = firstLink;
            else
            {
                forgeVersionFile.AdUrl = firstLink;
                forgeVersionFile.DirectUrl = secondLink;
            }

            files.Add(forgeVersionFile);
        }

        return files;
    }
}