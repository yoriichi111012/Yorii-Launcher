using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Installers;
using CmlLib.Utils;
using System.Text.Json;

namespace CmlLib.Core.Installer.Forge.Installers;

/* 1.12.2 - 1.20.* */
public class ForgeV12Installer : IForgeInstaller
{
    public ForgeV12Installer(string versionName, ForgeVersion forgeVersion)
    {
        VersionName = versionName;
        ForgeVersion = forgeVersion;
    }

    public string VersionName { get; }
    public ForgeVersion ForgeVersion { get; }

    public async Task Install(MinecraftPath path, IGameInstaller installer, ForgeInstallOptions options)
    {
        if (string.IsNullOrEmpty(options.JavaPath))
            throw new ArgumentNullException(nameof(options.JavaPath));
        var processor = new ForgeInstallProcessor(options.JavaPath);

        using var extractor = await ForgeInstallerExtractor.DownloadAndExtractInstaller(ForgeVersion, installer, options);
        using var installerProfileStream = extractor.OpenInstallerProfile();
        using var installerProfile = await JsonDocument.ParseAsync(installerProfileStream);

        await extractMavens(extractor.ExtractedDir, path);
        await installLibraries(installerProfile.RootElement, path, installer, options);
        await processor.MapAndStartProcessors(
            extractor.ExtractedDir, 
            path.GetVersionJarPath(ForgeVersion.MinecraftVersionName), 
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
        ForgeInstallOptions options)
    {
        if (installerProfile.TryGetProperty("libraries", out var libraryProp) &&
            libraryProp.ValueKind == JsonValueKind.Array)
        {
            var libraryInstaller = new ForgeLibraryInstaller(installer, options.RulesEvaluator, options.RulesContext, MojangServer.Library);
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
        var versionJsonDest = minecraftPath.GetVersionJsonPath(VersionName);
        IOUtil.CreateDirectoryForFile(versionJsonDest);
        await IOUtil.CopyFileAsync(versionJsonSource, versionJsonDest);

        var m = ForgeVersion.MinecraftVersionName;
        var f = ForgeVersion.ForgeVersionName;
        var jar = Path.Combine(installerDir, $"maven/net/minecraftforge/forge/{m}-{f}/forge-{m}-{f}.jar");
        if (File.Exists(jar)) //fix 1.17+ 
        {
            var jarPath = minecraftPath.GetVersionJarPath(VersionName);
            IOUtil.CreateDirectoryForFile(jarPath);
            await IOUtil.CopyFileAsync(jar, jarPath);
        }
    }
}
