using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Quiescent.Core.Commons.Serialization;

/// <summary>
/// Single choke point for all JSON serialization in Quiescent.* libraries.
///
/// Resolution order:
/// 1. Source-generated JsonSerializerContexts registered in
///    <see cref="JsonSerializerRegistry"/> (AOT-safe, preferred).
/// 2. Reflection-based fallback (JIT only; throws under NativeAOT for any
///    type missing from a registered context - which is the signal to add
///    a [JsonSerializable] annotation).
///
/// Do NOT add new direct JsonSerializer.Deserialize&lt;T&gt;/Serialize call sites
/// outside this class.
/// </summary>
public static class QuiescentJson
{
    internal static JsonSerializerOptions Options { get; } = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var resolvers = JsonSerializerRegistry.SnapshotResolvers();
        if (resolvers.Length == 0)
            return new JsonSerializerOptions();

        var combined = JsonTypeInfoResolver.Combine(resolvers);
        return new JsonSerializerOptions
        {
            TypeInfoResolver = combined,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    internal static bool TryGetTypeInfo<T>([NotNullWhen(true)] out JsonTypeInfo<T>? typeInfo)
    {
        if (JsonSerializerRegistry.TryGetTypeInfo(typeof(T), out var info) &&
            info is JsonTypeInfo<T> typed)
        {
            typeInfo = typed;
            return true;
        }
        typeInfo = null;
        return false;
    }

    // Fallbacks below are intentional JIT-only paths: under NativeAOT they
    // throw for types missing from a registered context, which is the signal
    // to add a [JsonSerializable] annotation. Each method carries its own
    // UnconditionalSuppressMessage (honored by both the Roslyn analyzers and
    // ILLink, unlike #pragma) so new trim-unsafe calls elsewhere still warn.

    // ---- string ----

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    public static T? Deserialize<T>(string json)
    {
        if (TryGetTypeInfo<T>(out var ti))
            return (T?)JsonSerializer.Deserialize(json, ti);
        return JsonSerializer.Deserialize<T>(json);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    public static string Serialize<T>(T value)
    {
        if (TryGetTypeInfo<T>(out var ti))
            return JsonSerializer.Serialize(value, ti);
        return JsonSerializer.Serialize(value);
    }

    // ---- stream ----

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    public static T? Deserialize<T>(Stream stream)
    {
        if (TryGetTypeInfo<T>(out var ti))
            return (T?)JsonSerializer.Deserialize(stream, ti);
        return JsonSerializer.Deserialize<T>(stream);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    public static ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        if (TryGetTypeInfo<T>(out var ti))
            return JsonSerializer.DeserializeAsync(stream, ti, ct);
        return JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    public static Task SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        if (TryGetTypeInfo<T>(out var ti))
            return JsonSerializer.SerializeAsync(stream, value, ti, ct);
        return JsonSerializer.SerializeAsync(stream, value, cancellationToken: ct);
    }

    // ---- JsonElement / JsonNode ----

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    public static T? Deserialize<T>(JsonElement element)
    {
        if (TryGetTypeInfo<T>(out var ti))
            return (T?)JsonSerializer.Deserialize(element, ti);
        return element.Deserialize<T>();
    }

    public static T? Deserialize<T>(JsonElement? element) =>
        element is JsonElement e ? Deserialize<T>(e) : default;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    public static T? Deserialize<T>(System.Text.Json.Nodes.JsonNode node)
    {
        if (TryGetTypeInfo<T>(out var ti))
            return (T?)JsonSerializer.Deserialize(node, ti);
        return node.Deserialize<T>();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    public static System.Text.Json.Nodes.JsonNode? SerializeToNode<T>(T value)
    {
        if (TryGetTypeInfo<T>(out var ti))
            return JsonSerializer.SerializeToNode(value, ti);
        return JsonSerializer.SerializeToNode(value);
    }

    // ---- reader/writer ----

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    public static T? Deserialize<T>(ref Utf8JsonReader reader)
    {
        if (TryGetTypeInfo<T>(out var ti))
            return JsonSerializer.Deserialize(ref reader, ti);
        return JsonSerializer.Deserialize<T>(ref reader);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Intentional JIT-only fallback; source-generated path is tried first.")]
    public static void Serialize<T>(Utf8JsonWriter writer, T value)
    {
        if (TryGetTypeInfo<T>(out var ti))
        {
            JsonSerializer.Serialize(writer, value, ti);
            return;
        }
        JsonSerializer.Serialize(writer, value);
    }
}
