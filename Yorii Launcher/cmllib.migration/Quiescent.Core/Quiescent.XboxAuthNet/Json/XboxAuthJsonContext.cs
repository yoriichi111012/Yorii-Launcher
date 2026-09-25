#if !QUIESCENT_SKIP_SOURCEGEN
using System.Text.Json.Serialization;
using Quiescent.Core.Commons.Serialization;
using Quiescent.XboxAuthNet.Jwt;
using Quiescent.XboxAuthNet.OAuth;
using Quiescent.XboxAuthNet.XboxLive.Requests;
using Quiescent.XboxAuthNet.XboxLive.Responses;

namespace Quiescent.XboxAuthNet.Json;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MicrosoftOAuthResponse))]
[JsonSerializable(typeof(MicrosoftUserPayload))]
[JsonSerializable(typeof(XboxErrorResponse))]
[JsonSerializable(typeof(XboxAuthResponse))]
[JsonSerializable(typeof(XboxSisuResponse))]
[JsonSerializable(typeof(XboxAuthXuiClaims))]
[JsonSerializable(typeof(XboxLiveAuthRequestBody))]
internal partial class XboxAuthJsonContext : JsonSerializerContext
{
    // the disable must precede the attribute: CA2255 is reported on the
    // attribute itself, so a pragma placed after it has no effect.
#pragma warning disable CA2255 // intentional: library self-registration of source-gen contexts
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        JsonSerializerRegistry.Register(XboxAuthJsonContext.Default);
#pragma warning restore CA2255

    // explicit entry point for AOT certainty (see CoreJsonContext).
    internal static void EnsureRegistered() =>
        JsonSerializerRegistry.Register(XboxAuthJsonContext.Default);
}

// public bootstrap so the app can force registration at startup without
// exposing the (internal-model-typed) source-gen context itself.
public static class XboxAuthJsonBootstrap
{
    public static void EnsureRegistered() => XboxAuthJsonContext.EnsureRegistered();
}
#endif
