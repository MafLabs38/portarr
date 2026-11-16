using System.Text.Json.Nodes;

namespace Portarr.Core.Catalog.Editing;

/// <summary>
/// What approving a waiting change writes: the keys the change touched, laid over the item as it is now, so a note edited
/// since does not block an approval. It is a conflict only when a key the change touches was itself changed since it was asked
/// for to something else than what the change says (the person then rejects it and asks again), or when the item appeared,
/// vanished or was removed in the meantime.
/// </summary>
public static class PendingMerge
{
    /// <summary>The item to write, or null to remove it; throws <see cref="CatalogEditException"/> with <see cref="CatalogEditStatus.Conflict"/>.</summary>
    public static JsonObject? Apply(string section, string id, JsonObject? before, JsonObject? after, JsonObject? now)
    {
        if (before is null || after is null || now is null)
        {
            // Added, removed, or gone: nothing to merge key by key, the item must still be what it was.
            return YamlNodes.Canonical(now) == YamlNodes.Canonical(before)
                ? after?.DeepClone().AsObject()
                : throw Conflict(section, id);
        }

        var merged = now.DeepClone().AsObject();
        var keys = before.Select(p => p.Key).Union(after.Select(p => p.Key), StringComparer.Ordinal).ToList();
        foreach (var key in keys.Where(k => YamlNodes.Canonical(before[k]) != YamlNodes.Canonical(after[k])))
        {
            var current = YamlNodes.Canonical(now[key]);
            if (current == YamlNodes.Canonical(after[key]))
            {
                continue; // somebody already made this very change: nothing to write, nothing to refuse
            }

            if (current != YamlNodes.Canonical(before[key]))
            {
                throw Conflict(section, id, key);
            }

            if (after[key] is { } value)
            {
                merged[key] = value.DeepClone();
            }
            else
            {
                merged.Remove(key);
            }
        }

        return merged;
    }

    private static CatalogEditException Conflict(string section, string id, string? key = null)
    {
        var what = key is null ? $"{section} '{id}'" : $"{section} '{id}', {key},";
        return new CatalogEditException(CatalogEditStatus.Conflict, $"{what} changed since this was asked for: reject it and ask again.");
    }
}
