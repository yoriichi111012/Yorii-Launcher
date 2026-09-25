using System.Text.Json;
using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Installers;
using CmlLib.Utils;
using ICSharpCode.SharpZipLib.Zip;

namespace CmlLib.Core.Installer.Forge.Installers;

/* 1.5.2 ~ 1.7.2 */
public class ForgeV5Installer : IForgeInstaller
{
    public ForgeV5Installer(string versionName, ForgeVersion forgeVersion)
    {
        VersionName = versionName;
        ForgeVersion = forgeVersion;
    }

    public string VersionName { get; }

    public ForgeVersion ForgeVersion { get; }

    public async Task Install(MinecraftPath path, IGameInstaller installer, ForgeInstallOptions options)
    {
        using var extractor = await ForgeInstallerExtractor.DownloadAndExtractInstaller(ForgeVersion, installer, options);
        using var installerProfileStream = extractor.OpenInstallerProfile();
        using var installerProfile = await JsonDocument.ParseAsync(installerProfileStream);

        var universalPath = getUniversalPath(installerProfile.RootElement, extractor.ExtractedDir);
        await copyUniversal(path, universalPath, installerProfile.RootElement);
        patchVanillaJar(path, universalPath);
        await installLibraries(installerProfile.RootElement, path, installer, options);

        var version = extractVersionJson(installerProfile.RootElement);
        await writeVersionJson(path, version);
    }
    
    private async Task installLibraries(
        JsonElement installerProfile, 
        MinecraftPath path, 
        IGameInstaller installer, 
        ForgeInstallOptions options)
    {
        if (installerProfile.TryGetProperty("versionInfo", out var versionInfo) &&
            versionInfo.TryGetProperty("libraries", out var libraryProp) &&
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
    
    private JsonElement extractVersionJson(JsonElement installerProfile)
    {
        JsonElement version;
        if (!installerProfile.TryGetProperty("versionInfo", out version))
            throw new InvalidOperationException("no versionInfo property");

        var versionId = version.GetPropertyValue("id");
        if (string.IsNullOrEmpty(versionId))
            throw new InvalidOperationException("install_profile.json does not have id property");

        return version;
    }

    private string getUniversalPath(JsonElement installerProfile, string installerDir)
    {
        var universalPath = installerProfile.GetPropertyOrNull("install")?.GetPropertyValue("filePath");
        if (string.IsNullOrEmpty(universalPath))
            throw new InvalidOperationException("filePath property in installer was null");
        universalPath = Path.Combine(installerDir, universalPath);
        return universalPath;
    }
    
    private async Task copyUniversal(MinecraftPath path, string universalPath, JsonElement installerProfile)
    {
        var targetPath = installerProfile.GetPropertyOrNull("install")?.GetPropertyValue("path");
        if (string.IsNullOrEmpty(targetPath))
            throw new InvalidOperationException("path property in installer was null");
        targetPath = ForgePackageName.GetPath(targetPath, Path.DirectorySeparatorChar);
        targetPath = Path.Combine(path.Library, targetPath);

        if (File.Exists(universalPath))
        {
            IOUtil.CreateDirectoryForFile(targetPath);
            await IOUtil.CopyFileAsync(universalPath, targetPath);
        }
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

    private async Task writeVersionJson(MinecraftPath path, JsonElement version)
    {
        var versionJsonPath = path.GetVersionJsonPath(VersionName);
        using var versionJsonStream = File.Create(versionJsonPath);
        await JsonSerializer.SerializeAsync(versionJsonStream, version);
    }
}