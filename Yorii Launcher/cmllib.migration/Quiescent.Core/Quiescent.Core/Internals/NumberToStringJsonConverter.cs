using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;

namespace Quiescent.Core.Internals;

/// <summary>
/// Reads JSON numbers OR strings into string properties (Mojang version
/// manifests mix both). Must be strongly typed to JsonConverter&lt;string?&gt;:
/// the source-gen serializer casts attribute-declared converters directly,
/// so an object?-based converter breaks under NativeAOT/source-gen.
/// </summary>
public class NumberToStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }
        return Encoding.UTF8.GetString(reader.ValueSpan.ToArray());
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
