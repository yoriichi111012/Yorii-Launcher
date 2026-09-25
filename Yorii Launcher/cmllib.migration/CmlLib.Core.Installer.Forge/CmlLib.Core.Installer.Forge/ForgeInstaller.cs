using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Installers;
using CmlLib.Core.Version;
using System.Diagnostics;

namespace CmlLib.Core.Installer.Forge;

public class ForgeInstaller
{
    public static readonly string ForgeAdUrl =
        "https://adfoc.us/serve/sitelinks/?id=271228&url=https://maven.minecraftforge.net/";

    private readonly MinecraftLauncher _launcher;
    private readonly IForgeInstallerVersionMapper _installerMapper;
    private readonly ForgeVersionLoader _versionLoader;

    public ForgeInstaller(MinecraftLauncher launcher) : this(launcher, HttpUtil.DefaultClient.Value)
    {

    }

    public ForgeInstaller(MinecraftLauncher launcher, HttpClient httpClient)
    {
        _installerMapper = new ForgeInstallerVersionMapper();
        _versionLoader = new ForgeVersionLoader(httpClient);
        _launcher = launcher;
    }

    public Task<string> Install(string mcVersion) =>
        Install(mcVersion, new ForgeInstallOptions());

    public async Task<string> Install(
        string mcVersion,
        ForgeInstallOptions options)
    {
        var versions = await _versionLoader.GetForgeVersions(mcVersion);
        var bestVersion =
            versions.FirstOrDefault(v => v.IsRecommendedVersion) ??
            versions.FirstOrDefault(v => v.IsLatestVersion) ??
            versions.FirstOrDefault() ??
            throw new InvalidOperationException("Cannot find any version");

        return await Install(bestVersion, options);
    }

    public Task<IEnumerable<ForgeVersion>> GetForgeVersions(string mcVersion)
    {
        return _versionLoader.GetForgeVersions(mcVersion);
    }

    public Task<string> Install(string mcVersion, string forgeVersion) =>
        Install(mcVersion, forgeVersion, new ForgeInstallOptions());

    public async Task<string> Install(
        string mcVersion,
        string forgeVersion,
        ForgeInstallOptions options)
    {
        var versions = await _versionLoader.GetForgeVersions(mcVersion);

        var foundVersion = versions.FirstOrDefault(v => v.ForgeVersionName == forgeVersion) ??
            throw new InvalidOperationException("Cannot find version name " + forgeVersion);
        return await Install(foundVersion, options);
    }

    public async Task<string> Install(
        ForgeVersion forgeVersion,
        ForgeInstallOptions options)
    {
        var installer = _installerMapper.CreateInstaller(forgeVersion);
        if (options.SkipIfAlreadyInstalled && await checkVersionInstalled(installer.VersionName))
            return installer.VersionName;

        var version = await checkAndDownloadVanillaVersion(
            forgeVersion.MinecraftVersionName,
            options.FileProgress,
            options.ByteProgress);

        if (string.IsNullOrEmpty(options.JavaPath))
            options.JavaPath = getJavaPath(version);

        await installer.Install(_launcher.MinecraftPath, _launcher.GameInstaller, options);
        showAd();
        await _launcher.GetAllVersionsAsync();
        return installer.VersionName;
    }

    private async Task<IVersion> checkAndDownloadVanillaVersion(
        string mcVersion,
        IProgress<InstallerProgressChangedEventArgs>? fileProgress,
        IProgress<ByteProgress>? byteProgress)
    {
        var version = await _launcher.GetVersionAsync(mcVersion);
        await _launcher.InstallAsync(version, fileProgress, byteProgress);
        return version;
    }

    private async Task<bool> checkVersionInstalled(string versionName)
    {
        try
        {
            await _launcher.GetVersionAsync(versionName);
            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private string getJavaPath(IVersion version)
    {
        var javaPath = _launcher.GetJavaPath(version);
        if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
            javaPath = _launcher.GetDefaultJavaPath();
        if (string.IsNullOrEmpty(javaPath) || !File.Exists(javaPath))
            throw new InvalidOperationException("Cannot find any java binary. Set java binary path");

        return javaPath;
    }

    private void showAd()
    {
        //########################AD URL##############################
        try
        {
            Process.Start(new ProcessStartInfo(ForgeAdUrl) { UseShellExecute = true });
        }
        catch
        {
            // ignore when url open failed
        }
        //########################AD URL##############################
    }
}
