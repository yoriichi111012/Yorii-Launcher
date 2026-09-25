namespace CmlLib.Core.Installer.NeoForge.Versions;

public class NeoForgeVersion
{
    public NeoForgeVersion(string minecraftVersion, string version)
    {
        MinecraftVersion = minecraftVersion;
        VersionName = version;
    }

    public string NeoForgeUrl =>
        $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{VersionName}/neoforge-{VersionName}-installer.jar";
    public string MinecraftVersion { get; set; }
    public string VersionName { get; }
    public IEnumerable<NeoForgeVersionFile>? Files { get; set; }

    public NeoForgeVersionFile? GetInstallerFile()
    {
        return new NeoForgeVersionFile
        {
            DirectUrl = NeoForgeUrl
        };
    }
}
