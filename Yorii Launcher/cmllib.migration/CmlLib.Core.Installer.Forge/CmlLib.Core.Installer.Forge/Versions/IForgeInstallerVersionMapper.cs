namespace CmlLib.Core.Installer.Forge.Versions;

public interface IForgeInstallerVersionMapper
{
    IForgeInstaller CreateInstaller(ForgeVersion version);
}
