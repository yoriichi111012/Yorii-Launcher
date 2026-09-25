using CmlLib.Core.FileExtractors;
using CmlLib.Core.Files;
using CmlLib.Core.Installers;
using CmlLib.Core.Rules;
using CmlLib.Core.Version;
using System.Text.Json;

namespace CmlLib.Core.Installer.Forge.Installers;

public class ForgeLibraryInstaller
{
    private readonly IRulesEvaluator _rulesEvaluator;
    private readonly RulesEvaluatorContext _rulesContext;
    private readonly IGameInstaller _installer;
    private readonly string _libraryServer;

    public ForgeLibraryInstaller(
        IGameInstaller installer,
        IRulesEvaluator rulesEvaluator,
        RulesEvaluatorContext context, 
        string libraryServer)
    {
        _installer = installer;
        _rulesEvaluator = rulesEvaluator;
        _rulesContext = context;
        _libraryServer = libraryServer;
    }

    public async Task Install(
        MinecraftPath path, 
        JsonElement element,
        IProgress<InstallerProgressChangedEventArgs>? fileProgress,
        IProgress<ByteProgress>? byteProgress,
        CancellationToken cancellationToken)
    {
        var libs = ExtractLibraries(element);
        var files = ExtractGameFile(path, libs);
        await _installer.Install(files, fileProgress, byteProgress, cancellationToken);
    }

    public IEnumerable<MLibrary> ExtractLibraries(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in element.EnumerateArray())
        {
            var lib = JsonLibraryParser.Parse(item);
            if (lib != null)
                yield return lib;
        }
    }

    public IEnumerable<GameFile> ExtractGameFile(MinecraftPath path, IEnumerable<MLibrary> libraries)
    {
        return libraries
            .Where(library => _rulesEvaluator.Match(library.Rules, _rulesContext))
            .SelectMany(library => LibraryFileExtractor.Extractor
                .ExtractTasks(_libraryServer, path, library, _rulesContext));
    }
}
