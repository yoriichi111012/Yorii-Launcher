using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Yorii_Launcher.Helpers;

internal static class LauncherYaml
{
    private static readonly LauncherYamlContext Context = new();

    private static readonly IDeserializer Deserializer = new StaticDeserializerBuilder(Context)
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        // themes are user made and might be from newer launcher so ignore unknown props
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new StaticSerializerBuilder(Context)
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static T? Deserialize<T>(string yaml) => Deserializer.Deserialize<T>(yaml);

    public static string Serialize<T>(T value) => Serializer.Serialize(value);
}
