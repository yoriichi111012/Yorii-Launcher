using System.Text.Json;
using System.Text.Json.Nodes;
using CmlLib.Core.Installer.NeoForge.Versions;
using CmlLib.Core.Installers;

namespace CmlLib.Core.Installer.NeoForge.Installers;

/* 1.20.2 - 1.21.* */
public class NeoFNewest : IForgeInstaller
{
    public NeoFNewest(string versionName, NeoForgeVersion neoForgeVersion)
    {
        VersionName = versionName;
        NeoForgeVersion = neoForgeVersion;
    }

    public string VersionName { get; }
    public NeoForgeVersion NeoForgeVersion { get; }

    public async Task Install(MinecraftPath path, IGameInstaller installer, NeoForgeInstallOptions options)
    {
        if (string.IsNullOrEmpty(options.JavaPath))
            throw new ArgumentNullException(nameof(options.JavaPath));
        var processor = new NeoForgeInstallProcessor(options.JavaPath);

        using var extractor = await NeoForgeInstallerExtractor.DownloadAndExtractInstaller(NeoForgeVersion, installer, options);
        using var installerProfileStream = extractor.OpenInstallerProfile();
        using var installerProfile = await JsonDocument.ParseAsync(installerProfileStream);

        await extractMavens(extractor.ExtractedDir, path);
        await installLibraries(installerProfile.RootElement, path, installer, options);
        await processor.MapAndStartProcessors(
            extractor.ExtractedDir, 
            path.GetVersionJarPath(NeoForgeVersion.MinecraftVersion), 
            path.Library, 
            installerProfile.RootElement, 
            options.FileProgress,
            options.InstallerOutput);
        await copyVersionFiles(extractor.ExtractedDir, path);
    }

    private async Task extractMavens(string installerPath, MinecraftPath minecraftPath)
    {
        var org = Path.Combine(installerPath, "maven");
        if (Directory.Exists(org))
            await IOUtil.CopyDirectory(org, minecraftPath.Library);
    }

    private async Task installLibraries(
        JsonElement installerProfile,
        MinecraftPath path,
        IGameInstaller installer,
        NeoForgeInstallOptions options)
    {
        if (installerProfile.TryGetProperty("libraries", out var libraryProp) &&
            libraryProp.ValueKind == JsonValueKind.Array)
        {
            var libraryInstaller = new NeoForgeLibraryInstaller(installer, options.RulesContext, MojangServer.Library);
            await libraryInstaller.Install(
                path,
                libraryProp,
                options.FileProgress,
                options.ByteProgress,
                options.CancellationToken);
        }
    }

    private async Task copyVersionFiles(string installerDir, MinecraftPath minecraftPath)
    {
        var versionJsonSource = Path.Combine(installerDir, "version.json");
        RemoveHotfixLibraries(versionJsonSource);
        var versionJsonDest = minecraftPath.GetVersionJsonPath(VersionName);
        IOUtil.CreateDirectoryForFile(versionJsonDest);
        await IOUtil.CopyFileAsync(versionJsonSource, versionJsonDest);

        var m = NeoForgeVersion.MinecraftVersion;
        var f = NeoForgeVersion.VersionName;
        var jar = Path.Combine(installerDir, $"maven/net/minecraftforge/forge/{m}-{f}/forge-{m}-{f}.jar");
        if (File.Exists(jar)) //fix 1.17+ 
        {
            var jarPath = minecraftPath.GetVersionJarPath(VersionName);
            IOUtil.CreateDirectoryForFile(jarPath);
            await IOUtil.CopyFileAsync(jar, jarPath);
        }
    }

    private void RemoveHotfixLibraries(string versionJsonSource)
    {
        string jsonString = File.ReadAllText(versionJsonSource);
        using JsonDocument doc = JsonDocument.Parse(jsonString);
        
        var libraries = doc.RootElement.GetProperty("libraries").EnumerateArray()
            .Where(item => item.GetProperty("name").GetString() != "org.apache.logging.log4j:log4j-slf4j2-impl:2.19.0@jar");
        
        var newLibraries = new JsonArray();
        foreach (var library in libraries)
        {
            newLibraries.Add(JsonNode.Parse(library.GetRawText()));
        }
        
        var root = new JsonObject();
        foreach (var item in doc.RootElement.EnumerateObject())
        {
            if (item.Name == "libraries")
            {
                root.Add(item.Name, newLibraries);
            }
            else
            {
                root.Add(item.Name, JsonNode.Parse(item.Value.GetRawText())); // используем JsonNode.Parse, вместо JsonDocument.Parse
            }
        }
        
        string newJson = root.ToString();

        File.WriteAllText(versionJsonSource, newJson);
    }
}
