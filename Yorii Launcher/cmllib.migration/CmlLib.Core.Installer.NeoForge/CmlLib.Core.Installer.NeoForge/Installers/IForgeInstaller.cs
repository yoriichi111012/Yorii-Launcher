using CmlLib.Core.Installer.NeoForge.Versions;
using CmlLib.Core.Installers;

namespace CmlLib.Core.Installer.NeoForge.Installers;

public interface IForgeInstaller
{
    string VersionName { get; }
    NeoForgeVersion NeoForgeVersion { get; }
    Task Install(MinecraftPath path, IGameInstaller installer, NeoForgeInstallOptions options);
}