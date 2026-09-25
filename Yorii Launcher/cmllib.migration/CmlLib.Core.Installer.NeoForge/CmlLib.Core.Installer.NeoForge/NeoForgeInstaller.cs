using System.Diagnostics;
using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.Installer.NeoForge.Versions;
using CmlLib.Core.Installers;
using CmlLib.Core.Version;

namespace CmlLib.Core.Installer.NeoForge;

public class NeoForgeInstaller
{
    public static readonly string ForgeAdUrl =
        "https://adfoc.us/serve/sitelinks/?id=271228&url=https://maven.minecraftforge.net/";

    private readonly MinecraftLauncher _launcher;
    private readonly IForgeInstallerVersionMapper _installerMapper;
    private readonly NeoForgeVersionLoader _versionLoader;

    public NeoForgeInstaller(MinecraftLauncher launcher)
    {
        _installerMapper = new NeoForgeInstallerVersionMapper();
        _versionLoader = new NeoForgeVersionLoader(new HttpClient());
        _launcher = launcher;
    }

    public Task<string> Install(string mcVersion) =>
        Install(mcVersion, new NeoForgeInstallOptions());

    public async Task<string> Install(
        string mcVersion,
        NeoForgeInstallOptions options)
    {
        var versions = await _versionLoader.GetNeoForgeVersions(mcVersion);
        var bestVersion =
            versions.FirstOrDefault() ??
            throw new InvalidOperationException("Cannot find any version");

        return await Install(bestVersion, options);
    }

    public Task<string> Install(string mcVersion, string neoForgeVersion) =>
        Install(mcVersion, neoForgeVersion, new NeoForgeInstallOptions());

    public async Task<string> Install(
        string mcVersion,
        string neoForgeVersion,
        NeoForgeInstallOptions options)
    {
        var versions = await _versionLoader.GetNeoForgeVersions(mcVersion);

        var foundVersion = versions.LastOrDefault(v => v.VersionName == neoForgeVersion) ??
            throw new InvalidOperationException("Cannot find version name " + neoForgeVersion);

        return await Install(foundVersion, options);
    }

    public async Task<string> Install(
        NeoForgeVersion neoForgeVersion,
        NeoForgeInstallOptions options)
    {
        var installer = _installerMapper.CreateInstaller(neoForgeVersion);
        if (options.SkipIfAlreadyInstalled && await checkVersionInstalled(installer.VersionName))
            return installer.VersionName;

        var version = await checkAndDownloadVanillaVersion(
            neoForgeVersion.MinecraftVersion,
            options.FileProgress,
            options.ByteProgress);

        if (string.IsNullOrEmpty(options.JavaPath))
            options.JavaPath = getJavaPath(version);

        await installer.Install(_launcher.MinecraftPath, _launcher.GameInstaller, options);

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

    public Task<IEnumerable<NeoForgeVersion>> GetForgeVersions(string mcVersion)
    {
        return _versionLoader.GetNeoForgeVersions(mcVersion);
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
        Process.Start(new ProcessStartInfo(ForgeAdUrl) { UseShellExecute = true });
        //########################AD URL##############################
    }
}
