using Quiescent.Core.Installer.NeoForge.Installers;

namespace Quiescent.Core.Installer.NeoForge.Versions;

public interface IForgeInstallerVersionMapper
{
    IForgeInstaller CreateInstaller(NeoForgeVersion version);
}
