using Quiescent.Core.Installer.Forge.Versions;
using Quiescent.Core.Installers;

namespace Quiescent.Core.Installer.Forge;

public interface IForgeInstaller
{
    string VersionName { get; }
    ForgeVersion ForgeVersion { get; }
    Task Install(MinecraftPath path, IGameInstaller installer, ForgeInstallOptions options);
}