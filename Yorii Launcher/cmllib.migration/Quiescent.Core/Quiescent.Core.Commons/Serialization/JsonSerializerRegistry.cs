using System.Text.Json.Serialization.Metadata;

namespace Quiescent.Core.Commons.Serialization;

/// <summary>
/// Registry of source-generated JsonSerializerContext instances.
/// Each Quiescent.* library registers its context via ModuleInitializer;
/// <see cref="QuiescentJson"/> resolves JsonTypeInfo through the combined
/// resolver so serialization is AOT-safe (no reflection fallback needed
/// when every serialized type is annotated).
///
/// Stored as IJsonTypeInfoResolver (JsonSerializerContext implements it)
/// to keep this assembly free of hard source-gen dependencies.
/// </summary>
public static class JsonSerializerRegistry
{
    private static readonly object _gate = new();
    private static readonly List<IJsonTypeInfoResolver> _resolvers = [];

    public static void Register(IJsonTypeInfoResolver resolver)
    {
        lock (_gate)
        {
            _resolvers.Add(resolver);
        }
    }

    internal static bool TryGetTypeInfo(Type type, out JsonTypeInfo? typeInfo)
    {
        IJsonTypeInfoResolver[] resolvers;
        lock (_gate)
            resolvers = _resolvers.ToArray();

        foreach (var resolver in resolvers)
        {
            var info = resolver.GetTypeInfo(type, QuiescentJson.Options);
            if (info != null)
            {
                typeInfo = info;
                return true;
            }
        }
        typeInfo = null;
        return false;
    }

    internal static IJsonTypeInfoResolver[] SnapshotResolvers()
    {
        lock (_gate)
            return _resolvers.ToArray();
    }
}
