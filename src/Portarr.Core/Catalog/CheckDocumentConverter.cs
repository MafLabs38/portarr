using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Portarr.Core.Catalog;

/// <summary>Reads <c>check: false</c>, <c>check: true</c> and <c>check: { … }</c> into one document type.</summary>
internal sealed class CheckDocumentConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(CheckDocument);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return bool.TryParse(scalar.Value, out var enabled)
                ? new CheckDocument { Enabled = enabled }
                : new CheckDocument { InvalidScalar = scalar.Value };
        }

        // A mapping: the strict deserializer still rejects unknown keys inside it.
        return new CheckDocument { Body = (CheckBodyDocument?)rootDeserializer(typeof(CheckBodyDocument)) };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        throw new NotSupportedException("The catalog is never written by Portarr.");
    }
}
