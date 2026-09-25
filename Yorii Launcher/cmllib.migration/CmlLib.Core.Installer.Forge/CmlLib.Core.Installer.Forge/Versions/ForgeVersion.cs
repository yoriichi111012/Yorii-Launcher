namespace CmlLib.Core.Installer.Forge.Versions;

public class ForgeVersion
{
    public ForgeVersion(string mcVersion, string forgeVersion)
    {
        this.MinecraftVersionName = mcVersion;
        this.ForgeVersionName = forgeVersion;
    }

    public string MinecraftVersionName { get; }
    public string ForgeVersionName { get; }
    public string? Time { get; set; }
    public IEnumerable<ForgeVersionFile> Files { get; set; } = [];
    public bool IsLatestVersion { get; set; }
    public bool IsRecommendedVersion { get; set; }

    public ForgeVersionFile? GetInstallerFile() 
        => Files.FirstOrDefault(file => file.Type?.ToLowerInvariant() == "installer");

    public ForgeVersionFile? GetUniversalFile() 
        => Files.FirstOrDefault(file => file.Type?.ToLowerInvariant() == "universal") ?? 
           Files.FirstOrDefault(file => file.Type?.ToLowerInvariant() == "client");
}
