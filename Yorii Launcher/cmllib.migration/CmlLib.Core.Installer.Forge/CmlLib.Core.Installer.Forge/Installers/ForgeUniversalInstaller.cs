using CmlLib.Core.Files;
using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Installers;
using CmlLib.Utils;
using ICSharpCode.SharpZipLib.Zip;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CmlLib.Core.Installer.Forge.Installers;

// 1.0 ~ 1.5.1
public class ForgeUniversalInstaller : IForgeInstaller
{
    public ForgeUniversalInstaller(string versionName, ForgeVersion forgeVersion)
    {
        VersionName = versionName;
        ForgeVersion = forgeVersion;
    }

    public string VersionName { get; }
    public ForgeVersion ForgeVersion { get; }

    public async Task Install(MinecraftPath path, IGameInstaller installer, ForgeInstallOptions options)
    {
        var universalPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "universal.zip");
        var universalUrl = ForgeVersion.GetUniversalFile()?.DirectUrl;

        if (string.IsNullOrEmpty(universalUrl))
        {
            throw new InvalidOperationException("Universal URL is not available.");
        }

        var file = new GameFile(ForgeVersion.ForgeVersionName)
        {
            Path = universalPath,
            Url = universalUrl,
            Hash = "",
        };
        await installer.Install([file], options.FileProgress, options.ByteProgress, options.CancellationToken);

        using var extractor = await ForgeInstallerExtractor.DownloadAndExtractUniversalInstaller(ForgeVersion, installer, options);
        patchVanillaJar(path, universalPath);
        await writeVersionJson(path);
    }

    private void patchVanillaJar(MinecraftPath path, string universalPath)
    {
        var vanillaJarPath = path.GetVersionJarPath(ForgeVersion.MinecraftVersionName);
        using var vanillaJarPatcher = JarPatcher.Extract(vanillaJarPath);
        vanillaJarPatcher.DeleteMetaInf();

        var zip = new FastZip();
        zip.ExtractZip(universalPath, vanillaJarPatcher.ExtractedPath, null);

        var forgeJarPath = path.GetVersionJarPath(VersionName);
        IOUtil.CreateDirectoryForFile(forgeJarPath);
        vanillaJarPatcher.CompressToJar(forgeJarPath);
    }

    private async Task writeVersionJson(MinecraftPath path)
    {
        var vanillaVersionJsonPath = path.GetVersionJsonPath(ForgeVersion.MinecraftVersionName);
        using var vanillaVersionJsonFile = File.OpenRead(vanillaVersionJsonPath);
        var vanillaVersionJson = await JsonNode.ParseAsync(vanillaVersionJsonFile);

        if (vanillaVersionJson == null)
        {
            throw new InvalidOperationException("The content of version json file was null");
        }

        vanillaVersionJson["id"] = VersionName;
        vanillaVersionJson["downloads"] = null;

        var forgeVersionJsonPath = path.GetVersionJsonPath(VersionName);
        IOUtil.CreateDirectoryForFile(forgeVersionJsonPath);
        using var forgeVersionJsonFile = File.Create(forgeVersionJsonPath);
        using var writer = new Utf8JsonWriter(forgeVersionJsonFile);
        vanillaVersionJson.WriteTo(writer);
    }
}
