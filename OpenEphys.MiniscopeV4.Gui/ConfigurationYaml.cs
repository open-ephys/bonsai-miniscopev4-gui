using System.IO;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Serializes and deserializes configurations to/from YAML.
/// </summary>
static class ConfigurationYaml
{
    static readonly ISerializer serializer = new SerializerBuilder()
        .WithTypeConverter(new DateTimeOffsetConverter())
        .Build();

    static readonly IDeserializer deserializer = new DeserializerBuilder()
        .WithTypeConverter(new DateTimeOffsetConverter())
        .Build();

    /// <summary>Serializes <paramref name="config"/> to a YAML string.</summary>
    public static string Serialize(object config) => serializer.Serialize(config);

    /// <summary>Deserializes an object of type <typeparamref name="T"/> from a YAML string.</summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="yaml">The YAML string to deserialize.</param>
    /// <returns>The deserialized object.</returns>
    public static T Deserialize<T>(string yaml)
    {
        var parser = new MergingParser(new Parser(new StringReader(yaml)));
        return deserializer.Deserialize<T>(parser);
    }
}
