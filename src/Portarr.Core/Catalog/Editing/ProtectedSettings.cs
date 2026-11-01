using System.Text.Json.Nodes;

namespace Portarr.Core.Catalog.Editing;

/// <summary>
/// The settings the catalog editor never changes (17-1): how Portarr reaches an environment (<c>runtime</c>, <c>reader</c>
/// and every section a connector owns: docker, kubernetes, cloudflare, backrest, hostMetrics…) and what it fetches for an
/// app (<c>check</c>). They hold an address and the name of the variable that holds the credential for it, so whoever can
/// write them can make Portarr send that credential to another host. Editing them is the operator's, in the catalog file;
/// the API and the MCP tools describe the fleet (notes, machines, links, providers, stacks…) and never reconfigure how it
/// is reached. An edit that carries none of them keeps the current ones; one that sets or removes them is refused.
/// </summary>
public static class ProtectedSettings
{
    public const string RuntimeKey = "runtime";
    public const string ReaderKey = "reader";
    public const string CheckKey = "check";

    /// <summary>The keys of a section's items that are protected; the connector sections are those the registered connectors own.</summary>
    public static IReadOnlyList<string> KeysOf(string section, IEnumerable<string> connectorSections) => section switch
    {
        CatalogSections.Environments => [RuntimeKey, ReaderKey, .. connectorSections],
        CatalogSections.Apps => [CheckKey],
        _ => [],
    };

    /// <summary>
    /// The item as it will be written: the protected settings are the current ones whatever the caller sent (kept when
    /// omitted). Throws when the caller sets, changes or removes one. A removed item is removed whole: that is an
    /// explicit act of its own.
    /// </summary>
    public static JsonObject? Apply(string section, string id, JsonObject? current, JsonObject? next, IReadOnlyList<string> keys)
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
            if (sent is null)
            {
                result = Restore(result, next, key, before);
            }
            else if (before is null || YamlNodes.Canonical(before) != YamlNodes.Canonical(sent))
            {
                refused.Add(key);
            }
        }

        if (refused.Count > 0)
        {
            throw new CatalogEditException(
                CatalogEditStatus.Invalid,
                $"{section} '{id}': {string.Join(", ", refused)} cannot be changed through the API or the MCP tools: they say how Portarr reaches a host and with which credential, so they are edited in the catalog file.");
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
