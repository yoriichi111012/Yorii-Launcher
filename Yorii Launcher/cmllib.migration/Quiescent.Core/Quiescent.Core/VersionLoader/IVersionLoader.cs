using Quiescent.Core.VersionMetadata;

namespace Quiescent.Core.VersionLoader
{
    public interface IVersionLoader
    {
        ValueTask<VersionMetadataCollection> GetVersionMetadatasAsync(CancellationToken cancellationToken = default);
    }
}
