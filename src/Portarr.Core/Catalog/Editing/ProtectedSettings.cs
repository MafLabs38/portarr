using System.Text.Json.Nodes;

namespace Portarr.Core.Catalog.Editing;

/// <summary>
/// The settings that say how Portarr reaches an environment (<c>runtime</c> and every section a connector owns: docker,
/// kubernetes, cloudflare, backrest, hostMetrics…) and what it fetches for an app (<c>check</c>). They hold an address and
/// the name of the variable that holds the credential for it, so whoever can write them can make Portarr send that
/// credential to another host. An MCP server's <c>http</c> and <c>stdio</c> are the same kind of setting (an address with its
/// credential variables, or a command the operator's machines will run, D-125). Declaring a machine needs them, so they can be written, but not by anyone alone: a person
/// holding <c>settings.manage</c> writes them directly, and any other author (an assistant, a key that only edits the
/// catalog) leaves a <b>pending change</b> that such a person approves in Settings (<see cref="CatalogPendingStore"/>). Every
/// change, approved or not, goes through <see cref="ConnectionGuardrails"/>. A read for an assistant leaves them out, and an
/// edit that omits them keeps the current ones.
/// </summary>
public static class ProtectedSettings
{
    public const string RuntimeKey = "runtime";
    public const string CheckKey = "check";
    public const string HttpKey = "http";
    public const string StdioKey = "stdio";

    /// <summary>An app's links: not protected (they are content), but the address check of an app with no <c>check</c> fetches them, so they pass the guardrails.</summary>
    public const string UrlsKey = "urls";

    /// <summary>An environment's address: not protected (it is how a person reads the host), but an app with no link of its own is probed there, so it passes the guardrails.</summary>
    public const string AddressKey = "address";

    /// <summary>The keys of a section's items that are protected; the connector sections are those the registered connectors own.</summary>
    public static IReadOnlyList<string> KeysOf(string section, IEnumerable<string> connectorSections) => section switch
    {
        CatalogSections.Environments => [RuntimeKey, .. connectorSections],
        CatalogSections.Apps => [CheckKey],
        CatalogSections.McpServers => [HttpKey, StdioKey],
        _ => [],
    };

    /// <summary>
    /// The keys whose addresses <see cref="ConnectionGuardrails"/> checks: the ones Portarr itself calls (the protected ones, and
    /// an app's links, which its default check fetches). The arguments of a
    /// local MCP program (<c>stdio</c>) may name a loopback address (a browser's debugging port): they wait for approval, no more.
    /// </summary>
    public static IReadOnlyList<string> GuardedKeysOf(string section, IEnumerable<string> connectorSections) =>
        section switch
        {
            CatalogSections.McpServers => [HttpKey],
            CatalogSections.Apps => [CheckKey, UrlsKey],
            CatalogSections.Environments => [.. KeysOf(section, connectorSections), AddressKey],
            _ => KeysOf(section, connectorSections),
        };

    /// <summary>Whether the edit sets, changes or removes a protected setting (removing an item that holds one counts).</summary>
    public static bool Touches(JsonObject? current, JsonObject? next, IReadOnlyList<string> keys)
    {
        // Removing an item that holds a connection setting removes that connection: it waits for approval like its addition did.
        return next is null ? keys.Any(key => current?[key] is not null) : ChangedKeys(current, next, keys).Count > 0;
    }

    /// <summary>The protected keys the edit sets, changes or removes: the guardrails check these, not what the item already held.</summary>
    public static IReadOnlyList<string> ChangedKeys(JsonObject? current, JsonObject? next, IReadOnlyList<string> keys)
    {
        return next is null
            ? []
            : [.. keys.Where(key => IsSetOrChanged(current, next, key) || IsRemoved(current, next, key))];
    }

    private static bool IsSetOrChanged(JsonObject? current, JsonObject next, string key)
    {
        return next[key] is { } sent && (current?[key] is not { } before || YamlNodes.Canonical(before) != YamlNodes.Canonical(sent));
    }

    /// <summary>A key sent as an explicit JSON <c>null</c> while the item holds it: the caller asks to remove it (a key left out means keep it).</summary>
    private static bool IsRemoved(JsonObject? current, JsonObject next, string key) => next.ContainsKey(key) && next[key] is null && current?[key] is not null;

    /// <summary>The item without its properties set to <c>null</c> (what a caller sends to remove a setting): they are never written.</summary>
    public static JsonObject? WithoutNulls(JsonObject? item)
    {
        return item is null || !item.Any(p => p.Value is null)
            ? item
            : new JsonObject(item.Where(p => p.Value is not null).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
    }

    /// <summary>
    /// The item as it will be written: a protected setting the caller left out is the current one (kept); one sent as an explicit
    /// <c>null</c> is removed. One the caller sets, changes or removes is refused unless <paramref name="allowChange"/> (the author
    /// may reconfigure, or it is an approval).
    /// </summary>
    public static JsonObject? Apply(string section, string id, JsonObject? current, JsonObject? next, IReadOnlyList<string> keys, bool allowChange = false)
    {
        if (next is null || keys.Count == 0)
        {
            return next;
        }

        var refused = new List<string>();
        var result = next;
        foreach (var key in keys)
        {
            var before = current?[key];
            var sent = next[key];
            if (sent is null && !next.ContainsKey(key))
            {
                result = Restore(result, next, key, before);
            }
            else if (!allowChange && (IsRemoved(current, next, key) || (sent is not null && (before is null || YamlNodes.Canonical(before) != YamlNodes.Canonical(sent)))))
            {
                refused.Add(key);
            }
        }

        if (refused.Count > 0)
        {
            throw new CatalogEditException(
                CatalogEditStatus.Invalid,
                $"{section} '{id}': {string.Join(", ", refused)} need an administrator's approval: they say how Portarr reaches a host and with which credential.");
        }

        return result;
    }

    /// <summary>The setting the caller left out, put back as it is; the sent object is copied the first time, never changed.</summary>
    private static JsonObject Restore(JsonObject result, JsonObject sent, string key, JsonNode? before)
    {
        if (before is null)
        {
            return result;
        }

        result = ReferenceEquals(result, sent) ? sent.DeepClone().AsObject() : result;
        result[key] = before.DeepClone();
        return result;
    }

    /// <summary>A copy of the item without its protected settings, for what an assistant reads (they name addresses and credential variables).</summary>
    public static JsonObject Redact(JsonObject item, IReadOnlyList<string> keys)
    {
        var copy = item.DeepClone().AsObject();
        foreach (var key in keys)
        {
            copy.Remove(key);
        }

        return copy;
    }
}
