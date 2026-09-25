using Quiescent.Core.Internals;
using System.Text.Json.Serialization;

namespace Quiescent.Core.Java;

public record JavaVersion
{
    public JavaVersion(string component) : this(component, null)
    {

    }

    [JsonConstructor]
    public JavaVersion(string component, string? majorVersion)
    {
        this.Component = component;
        this.MajorVersion = majorVersion;
    }

    [JsonPropertyName("component")]
    public string Component { get; }

    [JsonPropertyName("majorVersion")]
    [JsonConverter(typeof(NumberToStringConverter))]
    public string? MajorVersion { get; }
}