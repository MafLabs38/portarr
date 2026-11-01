using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Portarr.Core.Catalog.Editing;

/// <summary>Conversions between the YAML representation model and JSON nodes, and a canonical form to compare them.</summary>
internal static partial class YamlNodes
{
    [GeneratedRegex("^-?[0-9]{1,18}$")]
    private static partial Regex IntegerPattern();

    /// <summary>A YAML node as JSON: plain integers and booleans typed, the rest strings; empty and null scalars null.</summary>
    public static JsonNode? ToJson(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                var result = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    result[((YamlScalarNode)key).Value ?? string.Empty] = ToJson(value);
                }

                return result;
            case YamlSequenceNode sequence:
                return new JsonArray(sequence.Children.Select(ToJson).ToArray());
            case YamlScalarNode scalar when scalar.Style is ScalarStyle.Plain or ScalarStyle.Any:
                return Plain(scalar.Value);
            case YamlScalarNode scalar:
                return JsonValue.Create(scalar.Value ?? string.Empty);
            default:
                return null;
        }
    }

    private static JsonNode? Plain(string? value)
    {
        if (value is null or "" or "~" or "null" or "Null" or "NULL")
        {
            return null;
        }

        if (IntegerPattern().IsMatch(value))
        {
            return JsonValue.Create(long.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        return value switch
        {
            "true" or "True" or "TRUE" => JsonValue.Create(true),
            "false" or "False" or "FALSE" => JsonValue.Create(false),
            _ => JsonValue.Create(value),
        };
    }

    /// <summary>
    /// A comparable form: keys sorted, null values dropped, every scalar as its text. Two values with the same
    /// canonical form read the same for the catalog parser.
    /// </summary>
    public static string Canonical(JsonNode? node)
    {
        return Normalize(node)?.ToJsonString() ?? "null";
    }

    public static string Canonical(YamlNode node) => Canonical(ToJson(node));

    private static JsonNode? Normalize(JsonNode? node)
    {
        return node switch
        {
            JsonObject o => new JsonObject(o.Where(p => p.Value is not null)
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => KeyValuePair.Create(p.Key, Normalize(p.Value)))),
            JsonArray a => new JsonArray(a.Select(Normalize).ToArray()),
            JsonValue v => JsonValue.Create(v.GetValueKind() switch
            {
                JsonValueKind.String => v.GetValue<string>(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => v.ToJsonString(),
            }),
            _ => null,
        };
    }
}
