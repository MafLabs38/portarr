using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Portarr.Mcp.Server;

namespace Portarr.Mcp.Test;

/// <summary>
/// A tool answer is checked by the client against the output schema the tool announced. Portarr leaves null fields out of its
/// answers, so a field that can be null must not be announced as required: a client that validates (the Orchestrarr one does)
/// would reject the whole answer and report "no information" when the data exists.
/// </summary>
public sealed class McpOutputSchemaFixture
{
    private static IEnumerable<McpServerTool> Tools() =>
        new[] { typeof(PortarrTools), typeof(CatalogTools) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
                .Select(method => McpServerTool.Create(method, _ => null!, McpServiceCollectionExtensions.ToolOptions(null))));

    [Fact]
    public void should_not_announce_a_nullable_field_as_required()
    {
        var offenders = new List<string>();
        foreach (var tool in Tools())
        {
            if (tool.ProtocolTool.OutputSchema is { } schema)
            {
                Walk(JsonNode.Parse(schema.GetRawText())!, tool.ProtocolTool.Name, offenders);
            }
        }

        offenders.Should().BeEmpty("null fields are left out of the answers, so the schema must not require them");
    }

    private static void Walk(JsonNode node, string path, List<string> offenders)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject properties && obj["required"] is JsonArray required)
                {
                    foreach (var name in required.Select(r => r!.GetValue<string>()))
                    {
                        if (properties[name] is JsonObject property && AllowsNull(property))
                        {
                            offenders.Add($"{path}.{name}");
                        }
                    }
                }

                foreach (var (key, child) in obj)
                {
                    if (child is not null)
                    {
                        Walk(child, $"{path}/{key}", offenders);
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array.Where(c => c is not null))
                {
                    Walk(child!, path, offenders);
                }

                break;
        }
    }

    private static bool AllowsNull(JsonObject property) =>
        property["type"] switch
        {
            JsonArray types => types.Any(t => t?.GetValue<string>() == "null"),
            JsonValue value => value.GetValue<string>() == "null",
            _ => property["anyOf"] is JsonArray any && any.Any(a => a is JsonObject o && AllowsNull(o)),
        };
}
