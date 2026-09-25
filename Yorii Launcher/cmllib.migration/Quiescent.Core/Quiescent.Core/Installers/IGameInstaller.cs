using Quiescent.Core.Files;

namespace Quiescent.Core.Installers;

public interface IGameInstaller
{
    ValueTask Install(
        IEnumerable<GameFile> gameFiles,
        IProgress<InstallerProgressChangedEventArgs>? fileProgress,
        IProgress<ByteProgress>? byteProgress,
        CancellationToken cancellationToken);
}