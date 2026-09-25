using Quiescent.Core.Installer.NeoForge.Versions;
using Quiescent.Core.Installers;

namespace Quiescent.Core.Installer.NeoForge.Installers;

public interface IForgeInstaller
{
    string VersionName { get; }
    NeoForgeVersion NeoForgeVersion { get; }
    Task Install(MinecraftPath path, IGameInstaller installer, NeoForgeInstallOptions options);
}