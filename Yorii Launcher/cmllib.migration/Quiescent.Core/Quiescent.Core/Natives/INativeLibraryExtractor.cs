using Quiescent.Core.Rules;
using Quiescent.Core.Version;

namespace Quiescent.Core.Natives;

public interface INativeLibraryExtractor
{
    string Extract(MinecraftPath path, IVersion version, RulesEvaluatorContext rulesContext);
    void Clean(MinecraftPath path, IVersion version);
}