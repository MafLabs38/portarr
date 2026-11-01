using System.Collections.Concurrent;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Portarr.Core.Catalog;

/// <summary>
/// Reads an environment's own keys strictly and sets aside, as the events that make them up, the sections that connectors
/// own (D-105). The events keep their positions, so an error inside a section still names the line of the catalog.
/// </summary>
internal sealed class EnvironmentDocumentConverter : IYamlTypeConverter
{
    private readonly IReadOnlySet<string> _sections;
    private readonly IDeserializer _plain;

    public EnvironmentDocumentConverter(IReadOnlySet<string> sections, IDeserializer plain)
    {
        _sections = sections;
        _plain = plain;
    }

    public bool Accepts(Type type) => type == typeof(EnvironmentDocument);

    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var own = new List<ParsingEvent> { parser.Consume<MappingStart>() };
        var sections = new Dictionary<string, IReadOnlyList<ParsingEvent>>(StringComparer.Ordinal);
        MappingEnd end;
        while (!parser.TryConsume(out end!))
        {
            var key = parser.Consume<Scalar>();
            var value = ReadNode(parser);
            if (_sections.Contains(key.Value))
            {
                sections[key.Value] = value;
            }
            else
            {
                own.Add(key);
                own.AddRange(value);
            }
        }

        own.Add(end);
        var document = _plain.Deserialize<EnvironmentDocument>(new EventReplayParser(own));
        document.Sections = sections;
        return document;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        throw new NotSupportedException();
    }

    /// <summary>The events of one node: a scalar, or everything from its start to its end.</summary>
    private static List<ParsingEvent> ReadNode(IParser parser)
    {
        var events = new List<ParsingEvent>();
        var depth = 0;
        do
        {
            var current = parser.Consume<ParsingEvent>();
            events.Add(current);
            depth += current switch
            {
                MappingStart or SequenceStart => 1,
                MappingEnd or SequenceEnd => -1,
                _ => 0,
            };
        }
        while (depth > 0);

        return events;
    }
}

/// <summary>A parser over events already read, so a section can be deserialized into a connector's own document type.</summary>
internal sealed class EventReplayParser : IParser
{
    private readonly IReadOnlyList<ParsingEvent> _events;
    private int _index = -1;

    public EventReplayParser(IReadOnlyList<ParsingEvent> events)
    {
        _events = events;
    }

    public ParsingEvent? Current => _index >= 0 && _index < _events.Count ? _events[_index] : null;

    public bool MoveNext()
    {
        _index++;
        return _index < _events.Count;
    }
}

/// <summary>The catalog's deserializers: one per set of connector sections, built once.</summary>
internal static class CatalogDeserializers
{
    private static readonly ConcurrentDictionary<string, (IDeserializer Full, IDeserializer Plain)> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// <c>Full</c> reads a whole catalog, setting the connector sections aside; <c>Plain</c> reads one document of the catalog
    /// (an environment's own keys, a connector's section).
    /// </summary>
    public static (IDeserializer Full, IDeserializer Plain) For(IReadOnlySet<string> sections)
    {
        return Cache.GetOrAdd(string.Join('|', sections.Order(StringComparer.Ordinal)), _ => Create(sections));
    }

    private static (IDeserializer Full, IDeserializer Plain) Create(IReadOnlySet<string> sections)
    {
        var plain = Builder().Build();
        var full = Builder().WithTypeConverter(new EnvironmentDocumentConverter(sections, plain)).Build();
        return (full, plain);
    }

    private static DeserializerBuilder Builder()
    {
        return new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new CheckDocumentConverter());
    }
}
