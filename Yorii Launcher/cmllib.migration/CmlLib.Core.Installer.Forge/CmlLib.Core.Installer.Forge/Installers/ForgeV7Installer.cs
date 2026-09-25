using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Installers;
using CmlLib.Utils;
using System.Text.Json;

namespace CmlLib.Core.Installer.Forge.Installers;

/* 1.7.10 - 1.11.2 */
public class ForgeV7Installer : IForgeInstaller
{
    public ForgeV7Installer(string versionName, ForgeVersion forgeVersion)
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

        var version = extractVersionJson(installerProfile.RootElement);
        await extractUniversal(path, extractor.ExtractedDir, installerProfile.RootElement);
        await installLibraries(installerProfile.RootElement, path, installer, options);
        await writeVersionJson(path, version);
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

    private async Task extractUniversal(MinecraftPath path, string installerDir, JsonElement installerProfile)
    {
        var destPath = installerProfile.GetPropertyOrNull("install")?.GetPropertyValue("path");
        var universalPath = installerProfile.GetPropertyOrNull("install")?.GetPropertyValue("filePath");

        if (string.IsNullOrEmpty(universalPath))
            throw new InvalidOperationException("filePath property in installer was null");
        if (string.IsNullOrEmpty(destPath))
            throw new InvalidOperationException("path property in installer was null");

        var universal = Path.Combine(installerDir, universalPath);
        var desPath = ForgePackageName.GetPath(destPath, Path.DirectorySeparatorChar);
        var des = Path.Combine(path.Library, desPath);
        var jarPath = path.GetVersionJarPath(VersionName);

        if (File.Exists(universal))
        {
            IOUtil.CreateDirectoryForFile(jarPath);
            IOUtil.CreateDirectoryForFile(des);
            await IOUtil.CopyFileAsync(universal, des);
            await IOUtil.CopyFileAsync(universal, jarPath);
        }
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

    private async Task writeVersionJson(MinecraftPath path, JsonElement version)
    {
        var versionJsonPath = path.GetVersionJsonPath(VersionName);
        using var versionJsonStream = File.Create(versionJsonPath);
        await JsonSerializer.SerializeAsync(versionJsonStream, version);
    }
}
