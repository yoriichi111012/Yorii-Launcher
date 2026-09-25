using CmlLib.Core.Installer.Forge.Installers;

namespace CmlLib.Core.Installer.Forge.Versions;

public class ForgeInstallerVersionMapper : IForgeInstallerVersionMapper
{
    public IForgeInstaller CreateUniversal(string versionName, ForgeVersion version) =>
        new ForgeUniversalInstaller(versionName, version);

    public IForgeInstaller CreateV5(string versionName, ForgeVersion version) =>
        new ForgeV5Installer(versionName, version);

    public IForgeInstaller CreateV7(string versionName, ForgeVersion version) => 
        new ForgeV7Installer(versionName, version);

    public IForgeInstaller Create12(string versionName, ForgeVersion version) =>
        new ForgeV12Installer(versionName, version);

    public IForgeInstaller CreateInstaller(ForgeVersion version)
    {
        var m = version.MinecraftVersionName;
        var f = version.ForgeVersionName;
        var versionSplit = m.Split('.');
        var major = int.Parse(versionSplit[0]);
        var minor = int.Parse(versionSplit[1]);

        return (major, minor) switch
        {
            (1, <= 4) => (m, f) switch // oldest version ~ 1.4.7
            {
                _ => CreateUniversal($"{m}-Forge{f}", version)
            },
            (1, 5) => (m, f) switch // 1.5.x
            {
                ("1.5.2", _) => CreateV5($"1.5.2-Forge{f}", version),
                _ => CreateUniversal($"{m}-Forge{f}", version)
            },
            (1, 6) => (m, f) switch // 1.6.x
            {
                ("1.6.1", _) => CreateV5($"Forge{f}", version),
                _ => CreateV5($"{m}-Forge{f}", version)
            },
            (1, <= 12) => (m, f) switch // 1.7.2 ~ 1.12.2
            {
                ("1.7.2", _) => CreateV5($"1.7.2-Forge{f}-mc172", version),
                ("1.7.10-pre4", _) => CreateV7($"1.7.10-pre4-Forge{f}-prerelease", version),
                ("1.7.10", _) => CreateV7($"1.7.10-Forge{f}-1.7.10", version),

                ("1.8", _) => CreateV7(mf(m, f), version),
                ("1.8.8", _) => CreateV7(mf(m, f), version),
                ("1.8.9", _) => CreateV7(mfm(m, f), version),

                ("1.9", "12.16.1.1938") => CreateV7(mfm0(m, f), version),
                ("1.9", _) => CreateV7(mf(m, f), version),
                ("1.9.4", _) => CreateV7(mfm(m, f), version),

                ("1.10", _) => CreateV7(mfm0(m, f), version),
                ("1.10.2", _) or
                ("1.11", _) or
                ("1.11.2", _) or
                ("1.12", _) or
                ("1.12.1", _) => CreateV7(mf(m, f), version),
                ("1.12.2", _) => Create12($"1.12.2-forge-{f}", version),

                _ => CreateV7(mf(m, f), version)
            }, 
            _ => Create12($"{m}-forge-{f}", version) // 1.13.* ~ latest version
        };
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
