using CmlLib.Core.Installer.NeoForge.Installers;

namespace CmlLib.Core.Installer.NeoForge.Versions;

public class NeoForgeInstallerVersionMapper : IForgeInstallerVersionMapper
{
    public IForgeInstaller Create1_12_2Installer(string versionName, NeoForgeVersion version) =>
        new NeoFNewest(versionName, version);

    public IForgeInstaller CreateInstaller(NeoForgeVersion version)
    {
        var m = version.MinecraftVersion;
        var f = version.VersionName;

        return Create1_12_2Installer($"neoforge-{f}", version);
    }

    private string mf(string m, string f)
    {
        return $"{m}-forge{m}-{f}";
    }

    private string mfm(string m, string f)
    {
        return $"{m}-forge{m}-{f}-{m}";
    }

    private string mfm0(string m, string f)
    {
        return $"{m}-forge{m}-{f}-{m}.0";
    }
}
