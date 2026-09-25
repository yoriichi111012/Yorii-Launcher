#if !QUIESCENT_SKIP_SOURCEGEN
using System.Text.Json.Serialization;
using Quiescent.Core.Commons.Serialization;
using Quiescent.Core.Files;
using Quiescent.Core.ModLoaders.FabricMC;
using Quiescent.Core.ModLoaders.LiteLoader;
using Quiescent.Core.ModLoaders.QuiltMC;
using Quiescent.Core.Rules;
using Quiescent.Core.Version;
using Quiescent.Core.VersionMetadata;

namespace Quiescent.Core.Json;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonVersionDTO))]
[JsonSerializable(typeof(MFileMetadata))]
[JsonSerializable(typeof(MLogFileMetadata))]
[JsonSerializable(typeof(Dictionary<string, MFileMetadata>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(LauncherRule))]
[JsonSerializable(typeof(LiteLoaderVersion))]
[JsonSerializable(typeof(LiteLoaderVersionFile))]
[JsonSerializable(typeof(FabricLoader))]
[JsonSerializable(typeof(IEnumerable<FabricLoader>))]
[JsonSerializable(typeof(IReadOnlyCollection<FabricLoader>))]
[JsonSerializable(typeof(QuiltLoader))]
[JsonSerializable(typeof(IEnumerable<QuiltLoader>))]
[JsonSerializable(typeof(IReadOnlyCollection<QuiltLoader>))]
[JsonSerializable(typeof(JsonVersionManifestModel))]
internal partial class CoreJsonContext : JsonSerializerContext
{
    // the disable must precede the attribute: CA2255 is reported on the
    // attribute itself, so a pragma placed after it has no effect.
#pragma warning disable CA2255 // intentional: library self-registration of source-gen contexts
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        JsonSerializerRegistry.Register(CoreJsonContext.Default);
#pragma warning restore CA2255

    // explicit entry point for AOT certainty: ModuleInitializers alone are
    // fragile under trimming (a trimmed initializer = empty registry =
    // JsonTypeInfo failures at runtime). the app calls this at startup via
    // CoreJsonBootstrap; a statically-called method can never be trimmed
    // away. safe to call twice.
    internal static void EnsureRegistered() =>
        JsonSerializerRegistry.Register(CoreJsonContext.Default);
}

// public bootstrap so the app can force registration at startup without
// exposing the (internal-model-typed) source-gen context itself.
public static class CoreJsonBootstrap
{
    public static void EnsureRegistered() => CoreJsonContext.EnsureRegistered();
}
#endif
