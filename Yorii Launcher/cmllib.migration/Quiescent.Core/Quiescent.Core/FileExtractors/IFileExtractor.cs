using Quiescent.Core.Rules;
using Quiescent.Core.Files;
using Quiescent.Core.Version;

namespace Quiescent.Core.FileExtractors;

public interface IFileExtractor
{
    ValueTask<IEnumerable<GameFile>> Extract(
        MinecraftPath path,
        IVersion version,
        RulesEvaluatorContext rulesContext,
        CancellationToken cancellationToken);
}