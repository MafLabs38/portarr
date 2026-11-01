using System.Text;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Portarr.Core.Catalog.Editing;

/// <summary>The catalog text after an edit.</summary>
internal sealed record CatalogTextResult(string Text);

/// <summary>
/// Edits one item of a top-level list of the catalog (<c>apps</c>, <c>stacks</c>…) at the text level (D-83), so the
/// comments and formatting of everything else stay as the operator wrote them. YamlDotNet's representation model
/// drops comments but keeps the position of every node: the editor compares the new item with the old one and
/// splices only what changed into the original text: a changed value is re-emitted in place, a new key or list
/// item is inserted after the last one with the same indentation, a removed one loses its lines. A flow-style item
/// (<c>- { id: …, name: … }</c>) is one line: it is re-emitted whole, in flow style, unless it gains multi-line
/// text. Limits: comments inside a re-emitted node (a flow item spread over several lines, a list whose order
/// changed) are lost, and comment lines above a removed item stay. Every result is read back and compared with the
/// intended document; on any mismatch the whole item is re-emitted, and if that fails too nothing is written.
/// </summary>
internal sealed class CatalogTextEditor
{
    private readonly string _text;
    private readonly string _newline;
    private readonly List<(int Start, int End, string Text, int Order)> _edits = [];

    private CatalogTextEditor(string text)
    {
        _text = text;
        _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private enum Place
    {
        /// <summary>Inside a flow mapping or list: re-emitted on one line.</summary>
        Flow,

        /// <summary>The value of a key of a block mapping: the text from the key's end (the colon) is replaced.</summary>
        Value,

        /// <summary>An item of a block list: the text after <c>- </c> is replaced.</summary>
        Item,
    }

    /// <summary>
    /// The text with the item <paramref name="id"/> of <paramref name="section"/> replaced by <paramref name="item"/>,
    /// added when absent, or removed when <paramref name="item"/> is null.
    /// </summary>
    /// <exception cref="YamlException">The current text is not valid YAML.</exception>
    /// <exception cref="CatalogEditException">The file's shape cannot be edited (e.g. the root is not a mapping).</exception>
    public static CatalogTextResult Apply(string text, string section, string id, JsonObject? item)
    {
        var expected = Expected(text, section, id, item);

        var fine = new CatalogTextEditor(text).Edit(section, id, item, wholeItem: false);
        if (fine is not null && Matches(fine, expected))
        {
            return new CatalogTextResult(fine);
        }

        var whole = new CatalogTextEditor(text).Edit(section, id, item, wholeItem: true);
        if (whole is not null && Matches(whole, expected))
        {
            return new CatalogTextResult(whole);
        }

        throw new CatalogEditException(CatalogEditStatus.Invalid, $"The edit of {section} '{id}' could not be written without changing the rest of the file; edit the file by hand.");
    }

    /// <summary>
    /// The text with the top-level mapping <paramref name="key"/> (<c>portal:</c>, not a list) set to
    /// <paramref name="value"/>, added at the end of the file when absent, or removed when <paramref name="value"/> is null.
    /// Same guarantees as <see cref="Apply"/>.
    /// </summary>
    public static CatalogTextResult ApplySetting(string text, string key, JsonObject? value)
    {
        var root = Root(text) is { } mapping ? (JsonObject)YamlNodes.ToJson(mapping)! : new JsonObject();
        root.Remove(key);
        if (value is not null)
        {
            root[key] = value.DeepClone();
        }

        var expected = YamlNodes.Canonical(root);
        foreach (var whole in new[] { false, true })
        {
            var edited = new CatalogTextEditor(text).EditSetting(key, value, whole);
            if (edited is not null && Matches(edited, expected))
            {
                return new CatalogTextResult(edited);
            }
        }

        throw new CatalogEditException(CatalogEditStatus.Invalid, $"The edit of {key} could not be written without changing the rest of the file; edit the file by hand.");
    }

    private static YamlMappingNode? Root(string text)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(text);
        stream.Load(reader);
        if (stream.Documents.Count == 0)
        {
            return null;
        }

        return stream.Documents[0].RootNode switch
        {
            YamlMappingNode mapping => mapping,
            YamlScalarNode { Value: null or "" } => null,
            _ => throw new CatalogEditException(CatalogEditStatus.Invalid, "The catalog's root must be a mapping (environments:, apps:…)."),
        };
    }

    /// <summary>The whole document as JSON, as it must read after the edit.</summary>
    private static string Expected(string text, string section, string id, JsonObject? item)
    {
        var root = Root(text) is { } mapping ? (JsonObject)YamlNodes.ToJson(mapping)! : new JsonObject();
        var list = root[section] as JsonArray;
        if (list is null)
        {
            list = [];
            root[section] = list;
        }

        var index = list.Select((node, i) => (node, i)).FirstOrDefault(x => IdOf(x.node) == id, (null, -1)).Item2;
        if (index >= 0)
        {
            list.RemoveAt(index);
            if (item is not null)
            {
                list.Insert(index, item.DeepClone());
            }
        }
        else if (item is not null)
        {
            list.Add(item.DeepClone());
        }

        return YamlNodes.Canonical(root);
    }

    private static string? IdOf(JsonNode? node) => node is JsonObject o && o["id"] is JsonValue v ? v.ToString() : null;

    private static bool Matches(string text, string expected)
    {
        try
        {
            var root = Root(text);
            var actual = root is null ? new JsonObject() : (JsonObject)YamlNodes.ToJson(root)!;
            return YamlNodes.Canonical(actual) == expected
                || YamlNodes.Canonical(WithEmptyListsDropped(actual)) == YamlNodes.Canonical(WithEmptyListsDropped(JsonNode.Parse(expected)!.AsObject()));
        }
        catch (YamlException)
        {
            return false;
        }
    }

    // "links: []" and no "links" key read the same.
    private static JsonObject WithEmptyListsDropped(JsonObject root)
    {
        var copy = root.DeepClone().AsObject();
        foreach (var key in copy.Where(p => p.Value is JsonArray { Count: 0 }).Select(p => p.Key).ToList())
        {
            copy.Remove(key);
        }

        return copy;
    }

    private string? Edit(string section, string id, JsonObject? item, bool wholeItem)
    {
        var root = Root(_text);
        var pair = root?.Children.Where(p => p.Key is YamlScalarNode k && k.Value == section).Select(p => (KeyValuePair<YamlNode, YamlNode>?)p).FirstOrDefault();
        var list = pair?.Value as YamlSequenceNode;
        var existing = list?.Children.FirstOrDefault(n => n is YamlMappingNode m && m.Children.Any(c => c.Key is YamlScalarNode { Value: "id" } && c.Value is YamlScalarNode v && v.Value == id));

        if (existing is not null && list is not null)
        {
            if (item is null)
            {
                RemoveItem(pair!.Value.Key, list, existing);
            }
            else if (wholeItem || list.Style == SequenceStyle.Flow)
            {
                Replace(existing, item, list.Style == SequenceStyle.Flow ? Place.Flow : Place.Item, null);
            }
            else
            {
                Patch(existing, item, Place.Item, null);
            }
        }
        else if (item is not null)
        {
            AddItem(root, pair, section, item);
        }
        else
        {
            throw new CatalogEditException(CatalogEditStatus.NotFound, $"No {section} item '{id}' in the catalog.");
        }

        return Render();
    }

    private string? EditSetting(string key, JsonObject? value, bool whole)
    {
        var pair = Root(_text)?.Children.Where(p => p.Key is YamlScalarNode k && k.Value == key).Select(p => (KeyValuePair<YamlNode, YamlNode>?)p).FirstOrDefault();
        if (pair is { } found)
        {
            var name = (YamlScalarNode)found.Key;
            if (value is null)
            {
                DeleteLines(Start(name), PairEnd(name, found.Value));
            }
            else if (whole)
            {
                Replace(found.Value, value, Place.Value, name, block: true);
            }
            else
            {
                Patch(found.Value, value, Place.Value, name);
            }
        }
        else if (value is not null)
        {
            // A new section at the end of the file.
            var prefix = _text.Length == 0 || _text.EndsWith('\n') ? string.Empty : "\n";
            var gap = _text.Trim().Length == 0 ? string.Empty : "\n";
            Insert(_text.Length, prefix + gap + YamlEmitter.Key(key) + YamlEmitter.Value(value, 0, block: true) + "\n");
        }

        return Render();
    }

    private void AddItem(YamlMappingNode? root, KeyValuePair<YamlNode, YamlNode>? pair, string section, JsonObject item)
    {
        if (pair is not { } found)
        {
            // A new section at the end of the file, indented like the others.
            var dash = root?.Children.Values.OfType<YamlSequenceNode>().Where(s => s.Style != SequenceStyle.Flow).Select(s => Column(Start(s))).FirstOrDefault(2) ?? 2;
            var prefix = _text.Length == 0 || _text.EndsWith('\n') ? string.Empty : "\n";
            var gap = _text.Trim().Length == 0 ? string.Empty : "\n";
            Insert(_text.Length, prefix + gap + section + ":\n" + new string(' ', dash) + "- " + YamlEmitter.Item(item, dash + 2, flow: false) + "\n");
            return;
        }

        if (found.Value is YamlSequenceNode { Style: not SequenceStyle.Flow } list && list.Children.Count > 0 && DashOnOwnLine(list.Children[^1]))
        {
            var last = list.Children[^1];
            var column = Column(Start(last));
            var dash = DashIndex(last);
            // In the style most of the list is written in.
            var flow = list.Children.Count(c => c is YamlMappingNode { Style: MappingStyle.Flow }) * 2 > list.Children.Count;
            Insert(LineEnd(ContentEnd(last)), "\n" + new string(' ', Column(dash)) + "- " + YamlEmitter.Item(item, column, flow));
            return;
        }

        // An empty section (links:, links: []) or a flow list: the whole list is written again in block style.
        var items = found.Value is YamlSequenceNode sequence ? sequence.Children.Select(YamlNodes.ToJson).ToList() : [];
        items.Add(item.DeepClone());
        Replace(found.Value, new JsonArray(items.ToArray()), Place.Value, (YamlScalarNode)found.Key, block: true, itemFlow: found.Value is YamlSequenceNode { Children.Count: > 0 } ? null : false);
    }

    private void RemoveItem(YamlNode key, YamlSequenceNode list, YamlNode item)
    {
        if (list.Children.Count == 1 || list.Style == SequenceStyle.Flow || !DashOnOwnLine(item))
        {
            var rest = list.Children.Where(c => !ReferenceEquals(c, item)).Select(YamlNodes.ToJson).ToArray();
            Replace(list, new JsonArray(rest), Place.Value, (YamlScalarNode)key);
            return;
        }

        DeleteLines(DashIndex(item), ContentEnd(item));
    }

    /// <summary>Changes only what differs between <paramref name="old"/> and <paramref name="value"/>.</summary>
    private void Patch(YamlNode old, JsonNode value, Place place, YamlScalarNode? key)
    {
        if (YamlNodes.Canonical(old) == YamlNodes.Canonical(value))
        {
            return;
        }

        var patched = (old, value) switch
        {
            (YamlMappingNode { Style: not MappingStyle.Flow } mapping, JsonObject o) => PatchMapping(mapping, o),
            (YamlSequenceNode { Style: not SequenceStyle.Flow } sequence, JsonArray a) => PatchSequence(sequence, a),
            _ => false,
        };

        if (!patched)
        {
            Replace(old, value, place, key);
        }
    }

    private bool PatchMapping(YamlMappingNode mapping, JsonObject value)
    {
        var pairs = mapping.Children.Select(p => (Key: (YamlScalarNode)p.Key, p.Value)).ToList();
        var kept = pairs.Where(p => value[p.Key.Value!] is not null).ToList();
        var removed = pairs.Where(p => value[p.Key.Value!] is null).ToList();
        if (kept.Count == 0 || removed.Exists(p => !OnOwnLine(Start(p.Key))) || pairs.Exists(p => p.Key.Style is not (ScalarStyle.Plain or ScalarStyle.Any)))
        {
            return false;
        }

        foreach (var (key, old) in removed)
        {
            DeleteLines(Start(key), PairEnd(key, old));
        }

        foreach (var (key, old) in kept)
        {
            Patch(old, value[key.Value!]!, Place.Value, key);
        }

        var known = pairs.Select(p => p.Key.Value).ToHashSet(StringComparer.Ordinal);
        var added = value.Where(p => p.Value is not null && !known.Contains(p.Key)).ToList();
        if (added.Count > 0)
        {
            var column = Column(Start(pairs[0].Key));
            var anchor = kept[^1];
            Insert(
                LineEnd(PairEnd(anchor.Key, anchor.Value)),
                string.Concat(added.Select(p => "\n" + new string(' ', column) + YamlEmitter.Key(p.Key) + YamlEmitter.Value(p.Value, column))));
        }

        return true;
    }

    private bool PatchSequence(YamlSequenceNode sequence, JsonArray value)
    {
        var items = sequence.Children;
        if (!items.All(DashOnOwnLine))
        {
            return false;
        }

        if (items.Count == value.Count)
        {
            for (var i = 0; i < items.Count; i++)
            {
                Patch(items[i], value[i]!, Place.Item, null);
            }

            return true;
        }

        // Items kept in order, the others removed, the new ones appended: the kept items keep their comments.
        var kept = new List<YamlNode>();
        var next = 0;
        foreach (var item in items)
        {
            if (next < value.Count && YamlNodes.Canonical(item) == YamlNodes.Canonical(value[next]))
            {
                kept.Add(item);
                next++;
            }
            else
            {
                DeleteLines(DashIndex(item), ContentEnd(item));
            }
        }

        if (kept.Count == 0)
        {
            return false;
        }

        var last = kept[^1];
        var column = Column(Start(last));
        var dash = Column(DashIndex(last));
        var flow = last is YamlMappingNode { Style: MappingStyle.Flow };
        var appended = value.Skip(next).Select(v => "\n" + new string(' ', dash) + "- " + YamlEmitter.Item(v, column, flow));
        if (next < value.Count)
        {
            Insert(LineEnd(ContentEnd(last)), string.Concat(appended));
        }

        return true;
    }

    /// <summary>Writes <paramref name="value"/> in place of <paramref name="old"/>.</summary>
    private void Replace(YamlNode old, JsonNode value, Place place, YamlScalarNode? key, bool block = false, bool? itemFlow = null)
    {
        switch (place)
        {
            case Place.Value:
                var column = Column(Start(key!));
                var forceBlock = block || old is YamlMappingNode { Style: not MappingStyle.Flow } || old is YamlSequenceNode { Style: not SequenceStyle.Flow };
                AddBlockEdit(End(key!), PairEnd(key!, old), YamlEmitter.Value(value, column, forceBlock, itemFlow), column);
                break;
            case Place.Item:
                var itemColumn = Column(Start(old));
                AddBlockEdit(Start(old), ContentEnd(old), YamlEmitter.Item(value, itemColumn, flow: old is YamlMappingNode { Style: MappingStyle.Flow }), itemColumn);
                break;
            default:
                AddEdit(Start(old), ContentEnd(old), YamlEmitter.Flow(value));
                break;
        }
    }

    /// <summary>
    /// A replacement in block context. When it spans several lines, a comment that followed the old value on its
    /// line moves to a line of its own: after a literal block it would otherwise become part of the text.
    /// </summary>
    private void AddBlockEdit(int start, int end, string text, int column)
    {
        var lineEnd = LineEnd(end);
        var rest = _text.AsSpan(end, lineEnd - end);
        var blanks = rest.Length - rest.TrimStart().Length;
        if (text.Contains('\n', StringComparison.Ordinal) && rest.TrimStart().StartsWith("#", StringComparison.Ordinal))
        {
            AddEdit(start, end + blanks, text + "\n" + new string(' ', column));
            return;
        }

        AddEdit(start, end, text);
    }

    private void Insert(int at, string text) => AddEdit(at, at, text);

    private void AddEdit(int start, int end, string text) => _edits.Add((start, end, text, _edits.Count));

    /// <summary>Removes the whole lines from the one holding <paramref name="start"/> to the one holding <paramref name="end"/>.</summary>
    private void DeleteLines(int start, int end)
    {
        var lineEnd = LineEnd(end);
        var next = lineEnd < _text.Length ? _text.IndexOf('\n', lineEnd) + 1 : _text.Length;
        AddEdit(LineStart(start), next == 0 ? _text.Length : next, string.Empty);
    }

    private string? Render()
    {
        // Applied from the end so earlier positions stay valid; insertions at one position keep their order.
        var ordered = _edits.OrderBy(e => e.Start).ThenBy(e => e.Order).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start < ordered[i - 1].End)
            {
                return null;
            }
        }

        var builder = new StringBuilder(_text);
        foreach (var (start, end, text, _) in ordered.OrderByDescending(e => e.Start).ThenByDescending(e => e.Order))
        {
            builder.Remove(start, end - start).Insert(start, _newline == "\n" ? text : text.Replace("\n", _newline, StringComparison.Ordinal));
        }

        return builder.ToString();
    }

    private static int Start(YamlNode node) => checked((int)node.Start.Index);

    private static int End(YamlNode node) => checked((int)node.End.Index);

    /// <summary>Where a node's own text ends: block collections end with their last child, trailing blanks are not part of it.</summary>
    private int ContentEnd(YamlNode node)
    {
        // The representation model gives collections no end mark: block ones end with their last child, flow ones
        // at their closing bracket.
        var end = node switch
        {
            YamlMappingNode { Style: MappingStyle.Flow } m => Closing(m.Children.Count > 0 ? PairEnd((YamlScalarNode)m.Children.Last().Key, m.Children.Last().Value) : Start(m) + 1, '}'),
            YamlSequenceNode { Style: SequenceStyle.Flow } s => Closing(s.Children.Count > 0 ? ContentEnd(s.Children[^1]) : Start(s) + 1, ']'),
            YamlMappingNode m when m.Children.Count > 0 => PairEnd((YamlScalarNode)m.Children.Last().Key, m.Children.Last().Value),
            YamlSequenceNode s when s.Children.Count > 0 => ContentEnd(s.Children[^1]),
            _ => End(node),
        };

        while (end > Start(node) && char.IsWhiteSpace(_text[end - 1]))
        {
            end--;
        }

        return end;
    }

    /// <summary>Just after the bracket closing a flow collection, searched from its last child (commas, blanks and comments skipped).</summary>
    private int Closing(int from, char bracket)
    {
        var i = from;
        while (i < _text.Length)
        {
            if (_text[i] == bracket)
            {
                return i + 1;
            }

            if (_text[i] == '#')
            {
                // A comment runs to the end of its line.
                i = LineEnd(i) + 1;
            }
            else if (!char.IsWhiteSpace(_text[i]) && _text[i] != ',')
            {
                break;
            }
            else
            {
                i++;
            }
        }

        throw new CatalogEditException(CatalogEditStatus.Invalid, "A flow collection of the catalog could not be read back; edit the file by hand.");
    }

    /// <summary>The end of a <c>key: value</c> pair; for an empty value (<c>key:</c>), just after the colon.</summary>
    private int PairEnd(YamlScalarNode key, YamlNode value)
    {
        if (value is YamlScalarNode { Value: null or "", Style: ScalarStyle.Plain or ScalarStyle.Any })
        {
            return _text.IndexOf(':', End(key)) + 1;
        }

        return ContentEnd(value);
    }

    private int LineStart(int index) => index == 0 ? 0 : _text.LastIndexOf('\n', index - 1) + 1;

    private int LineEnd(int index)
    {
        var end = _text.IndexOf('\n', index);
        if (end < 0)
        {
            return _text.Length;
        }

        return end > 0 && _text[end - 1] == '\r' ? end - 1 : end;
    }

    private int Column(int index) => index - LineStart(index);

    private bool OnOwnLine(int index) => _text.AsSpan(LineStart(index), index - LineStart(index)).IsWhiteSpace();

    /// <summary>The dash of a block list item: the last <c>-</c> before the item's text.</summary>
    private int DashIndex(YamlNode item) => _text.LastIndexOf('-', Start(item) - 1);

    private bool DashOnOwnLine(YamlNode item)
    {
        var dash = DashIndex(item);
        return dash >= 0 && OnOwnLine(dash) && _text.AsSpan(dash + 1, Start(item) - dash - 1).IsWhiteSpace();
    }
}
