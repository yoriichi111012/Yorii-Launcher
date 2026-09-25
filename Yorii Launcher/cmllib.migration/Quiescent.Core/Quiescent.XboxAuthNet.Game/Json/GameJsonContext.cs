#if !QUIESCENT_SKIP_SOURCEGEN
using System.Text.Json.Serialization;
using Quiescent.Core.Commons.Serialization;
using Quiescent.XboxAuthNet.OAuth;
using Quiescent.XboxAuthNet.Game.XboxAuth;

namespace Quiescent.XboxAuthNet.Game.Json;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MicrosoftOAuthResponse))]
[JsonSerializable(typeof(XboxAuthTokens))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DateTime))]
internal partial class GameJsonContext : JsonSerializerContext
{
    // the disable must precede the attribute: CA2255 is reported on the
    // attribute itself, so a pragma placed after it has no effect.
#pragma warning disable CA2255 // intentional: library self-registration of source-gen contexts
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        JsonSerializerRegistry.Register(GameJsonContext.Default);
#pragma warning restore CA2255

    // explicit entry point for AOT certainty (see CoreJsonContext).
    internal static void EnsureRegistered() =>
        JsonSerializerRegistry.Register(GameJsonContext.Default);
}

// public bootstrap so the app can force registration at startup without
// exposing the (internal-model-typed) source-gen context itself.
public static class GameJsonBootstrap
{
    public static void EnsureRegistered() => GameJsonContext.EnsureRegistered();
}
#endif
