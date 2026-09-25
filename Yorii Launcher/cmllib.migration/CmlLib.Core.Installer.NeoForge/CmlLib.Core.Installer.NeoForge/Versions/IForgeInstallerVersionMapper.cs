using CmlLib.Core.Installer.NeoForge.Installers;

namespace CmlLib.Core.Installer.NeoForge.Versions;

public interface IForgeInstallerVersionMapper
{
    IForgeInstaller CreateInstaller(NeoForgeVersion version);
}
