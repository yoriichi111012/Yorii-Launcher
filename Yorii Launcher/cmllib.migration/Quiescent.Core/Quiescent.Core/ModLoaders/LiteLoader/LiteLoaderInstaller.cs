using Quiescent.Core.CommandParser;
using Quiescent.Core.Files;
using Quiescent.Core.Internals;
using Quiescent.Core.ProcessBuilder;
using Quiescent.Core.Version;
using Quiescent.Core.VersionLoader;
using Quiescent.Core.VersionMetadata;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Quiescent.Core.ModLoaders.LiteLoader;

// 1.8.9 freezing
public class LiteLoaderInstaller
{
    public static readonly string LiteLoaderLibName = "com.mumfrey:liteloader";
    public static readonly string ManifestServer = "http://dl.liteloader.com/versions/versions.json";
    public static readonly string DownloadServer = "http://dl.liteloader.com/versions/"; 

    private readonly HttpClient _httpClient;
    private readonly string _manifestServer;

    public LiteLoaderInstaller(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _manifestServer = ManifestServer;
    }

    public LiteLoaderInstaller(HttpClient httpClient, string manifestServer)
    {
        _httpClient = httpClient;
        _manifestServer = manifestServer;
    }

    public static string GetVersionName(string loaderVersion, string gameVersion)
    {
        loaderVersion = loaderVersion.Replace("LiteLoader", "");
        return $"{loaderVersion}-LiteLoader{gameVersion}";
    }

    public async Task<IReadOnlyList<LiteLoaderVersion>> GetAllLiteLoaders()
    {
        using var res = await _httpClient.GetStreamAsync(_manifestServer);
        using var json = await JsonDocument.ParseAsync(res);
        var versions = parseVersions(json.RootElement);
        return versions.ToList();
    }

    private IEnumerable<LiteLoaderVersion> parseVersions(JsonElement root)
    {
        if (root.TryGetProperty("versions", out var versions))
        {
            foreach (var item in versions.EnumerateObject())
            {
                var baseVersion = item.Name;
                var versionObj = item.Value;

                var libObj =
                    versionObj.GetPropertyOrNull("artefacts") ??
                    versionObj.GetPropertyOrNull("snapshots");

                var latestLiteLoader = Quiescent.Core.Commons.Serialization.QuiescentJson.Deserialize<LiteLoaderVersion>(
                    libObj?
                        .GetPropertyOrNull(LiteLoaderLibName)?
                        .GetPropertyOrNull("latest"));

                if (latestLiteLoader == null)
                    continue;

                latestLiteLoader.BaseVersion = baseVersion;
                yield return latestLiteLoader;
            }
        }
    }

    public async Task<string> Install(LiteLoaderVersion loader, IVersion baseVersion, MinecraftPath path)
    {
        if (string.IsNullOrEmpty(loader.Version))
            throw new ArgumentException("invalid loader");

        var versionName = GetVersionName(loader.Version, baseVersion.Id);
        var versionJsonFilePath = path.GetVersionJsonPath(versionName);
        IOUtil.CreateParentDirectory(versionJsonFilePath);
        using var versionJsonFile = File.Create(versionJsonFilePath);

        IEnumerable<LiteLoaderLibrary> createLibraries()
        {
            yield return new LiteLoaderLibrary
            {
                Name = $"{LiteLoaderLibName}:{loader.Version}",
                Url = DownloadServer
            };

            foreach (var lib in loader.Libraries ?? Enumerable.Empty<LiteLoaderLibrary>())
            {
                // asm-all:5.2 is only available on LiteLoader server
                if (lib.Name == "org.ow2.asm:asm-all:5.2")
                    lib.Url = "http://repo.liteloader.com/";
                yield return lib;
            }
        }

        IEnumerable<string> createGameArguments()
        {
            if (!string.IsNullOrEmpty(loader.TweakClass))
            {
                yield return "--tweakClass";
                yield return loader.TweakClass;
            }

            var args = baseVersion.GetGameArguments(false).SelectMany(arg => arg.Values);
            foreach (var item in args)
                yield return item;
        }

        await Quiescent.Core.Commons.Serialization.QuiescentJson.SerializeAsync(versionJsonFile, new LiteLoaderVersionFile(
            versionName, baseVersion.Id, createLibraries().ToArray(), string.Join(" ", createGameArguments())));
        return versionName;
    }

}

/// <summary>
/// AOT-safe named payload replacing the anonymous object previously
/// serialized as the installed LiteLoader version JSON.
/// </summary>
public sealed record LiteLoaderVersionFile(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] string Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("inheritsFrom")] string InheritsFrom,
    [property: System.Text.Json.Serialization.JsonPropertyName("libraries")] IEnumerable<LiteLoaderLibrary> Libraries,
    [property: System.Text.Json.Serialization.JsonPropertyName("minecraftArguments")] string MinecraftArguments)
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type => "release";

    [System.Text.Json.Serialization.JsonPropertyName("mainClass")]
    public string MainClass => "net.minecraft.launchwrapper.Launch";

    [System.Text.Json.Serialization.JsonPropertyName("jar")]
    public string Jar => InheritsFrom;
}