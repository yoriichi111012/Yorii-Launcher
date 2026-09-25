using Quiescent.Core.Files;

namespace Quiescent.Core.Tasks;

public interface IUpdateTask
{
    ValueTask Execute(GameFile file, CancellationToken cancellationToken);
}