using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Portarr.Core.Catalog.Editing;

/// <summary>
/// Writes catalog values (JSON nodes) as YAML in the catalog's own style (D-83): short mappings and lists in flow
/// style on one line (<c>{ id: x, name: y }</c>), longer ones in block style, multi-line text as a literal block.
/// Strings are left plain when YAML reads them back as the same string, else double-quoted; URLs are quoted.
/// Null values are left out: a missing key and a null key mean the same in the catalog.
/// </summary>
internal static partial class YamlEmitter
{
    /// <summary>Longest flow mapping or list kept on the value's line before switching to block style.</summary>
    private const int InlineWidth = 100;

    /// <summary>Longest list item kept on the dash's line (<c>- { app: x, role: critical }</c>).</summary>
    private const int ItemWidth = 160;

    [GeneratedRegex(@"^[\p{L}\p{N}_./~+()$-](?:[\p{L}\p{N} _./~@+()=$-]*[\p{L}\p{N}_./~@+()=$-])?$")]
    private static partial Regex PlainPattern();

    [GeneratedRegex(@"^(?:[-+]?(?:\d[\d_]*)?(?:\.\d*)?(?:[eE][-+]?\d+)?|0x[0-9a-fA-F]+|0o[0-7]+|\.inf|\.nan|true|false|yes|no|on|off|y|n|null|~)$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedPattern();

    /// <summary>A value on one line, valid in flow and block context.</summary>
    public static string Flow(JsonNode? node)
    {
        return node switch
        {
            JsonObject o when !o.Any(p => p.Value is not null) => "{}",
            JsonObject o => "{ " + string.Join(", ", o.Where(p => p.Value is not null).Select(p => $"{Key(p.Key)}: {Flow(p.Value)}")) + " }",
            JsonArray a => "[" + string.Join(", ", a.Select(Flow)) + "]",
            JsonValue v => Scalar(v),
            _ => "null",
        };
    }

    /// <summary>Whether a value holds text on several lines (never written in flow style when it can be avoided).</summary>
    public static bool HasMultilineText(JsonNode? node)
    {
        return node switch
        {
            JsonObject o => o.Any(p => HasMultilineText(p.Value)),
            JsonArray a => a.Any(HasMultilineText),
            JsonValue v => v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Contains('\n', StringComparison.Ordinal),
            _ => false,
        };
    }

    /// <summary>
    /// A list item placed after <c>- </c>. A mapping is written on one line when <paramref name="flow"/> is true
    /// (the style of its neighbours) or, when null, when it is short; else as a block mapping whose first key
    /// follows the dash and whose other lines start at <paramref name="column"/>. Multi-line text is never inline.
    /// </summary>
    public static string Item(JsonNode? node, int column, bool? flow)
    {
        if (node is JsonObject o && o.Any(p => p.Value is not null))
        {
            var inline = !HasMultilineText(o) && (flow ?? Flow(o).Length <= ItemWidth);
            return inline ? Flow(o) : Mapping(o, column);
        }

        return node is JsonValue v && IsLiteral(v) ? Literal(v.GetValue<string>(), column) : Flow(node);
    }

    /// <summary>A block mapping: the first key unindented (the caller placed it), the next ones at <paramref name="column"/>.</summary>
    public static string Mapping(JsonObject node, int column)
    {
        var lines = node.Where(p => p.Value is not null).Select(p => Key(p.Key) + Value(p.Value, column));
        return string.Join("\n" + new string(' ', column), lines);
    }

    /// <summary>What follows <c>key</c> for a value whose key is at <paramref name="column"/>: <c>: x</c>, or <c>:</c> and indented lines.</summary>
    public static string Value(JsonNode? node, int column, bool block = false, bool? itemFlow = null)
    {
        var inner = column + 2;
        var pad = "\n" + new string(' ', inner);
        switch (node)
        {
            case JsonValue v when IsLiteral(v):
                return ": " + Literal(v.GetValue<string>(), inner);
            case JsonObject o when o.Any(p => p.Value is not null) && (block || HasMultilineText(o) || Flow(o).Length > InlineWidth):
                return ":" + pad + Mapping(o, inner);
            case JsonArray a when a.Count > 0 && (block || HasMultilineText(a) || Flow(a).Length > InlineWidth || a.Any(i => i is JsonObject)):
                return ":" + string.Concat(a.Select(i => pad + "- " + Item(i, inner + 2, itemFlow)));
            default:
                return ": " + Flow(node);
        }
    }

    public static string Key(string key) => PlainPattern().IsMatch(key) && !ReservedPattern().IsMatch(key) ? key : Quote(key);

    private static string Scalar(JsonValue value)
    {
        return value.GetValueKind() switch
        {
            JsonValueKind.String => String(value.GetValue<string>()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.ToJsonString(),
            _ => "null",
        };
    }

    private static string String(string text)
    {
        var plain = PlainPattern().IsMatch(text)
            && !ReservedPattern().IsMatch(text)
            && !text.Contains("  ", StringComparison.Ordinal)
            && !text.StartsWith("- ", StringComparison.Ordinal);
        return plain ? text : Quote(text);
    }

    /// <summary>Text that reads better as a literal block: several lines, nothing a block would change.</summary>
    private static bool IsLiteral(JsonValue value)
    {
        if (value.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetValue<string>();
        if (!text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal) || text.StartsWith(' ') || text.StartsWith('\n'))
        {
            return false;
        }

        var body = text.EndsWith('\n') ? text[..^1] : text;
        return !body.EndsWith('\n')
            && body.Split('\n').All(line => line.Length == 0 || (!char.IsWhiteSpace(line[^1]) && !line.Contains('\t', StringComparison.Ordinal)))
            && !body.Any(c => char.IsControl(c) && c != '\n');
    }

    private static string Literal(string text, int column)
    {
        var indicator = text.EndsWith('\n') ? "|" : "|-";
        var body = text.EndsWith('\n') ? text[..^1] : text;
        var pad = new string(' ', column);
        return indicator + string.Concat(body.Split('\n').Select(line => line.Length == 0 ? "\n" : "\n" + pad + line));
    }

    private static string Quote(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }
}
