using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;

namespace Portarr.Core.Catalog.Editing;

/// <summary>
/// A change that waits for an administrator: who asked, for which item, the item as it was and as it would be. <c>After</c>
/// is null for a removal. Never a secret: the catalog holds none.
/// </summary>
public sealed record PendingChange(
    string Id,
    DateTimeOffset At,
    string Channel,
    string? User,
    string? Via,
    string Section,
    string ItemId,
    string Operation,
    JsonObject? Before,
    JsonObject? After,
    IReadOnlyList<string> Changes);

/// <summary>
/// The changes waiting for approval, kept next to the catalog (<c>catalog.pending.json</c>, like the audit trail and the
/// backups): they survive a restart and live with the file they would change. Not thread-safe by itself: the editor calls
/// it under its write lock. Capped, so an assistant that keeps asking cannot fill the disk.
/// </summary>
public sealed class CatalogPendingStore
{
    public const int Capacity = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    public CatalogPendingStore(IOptions<PortarrOptions> options)
    {
        var catalog = Path.GetFullPath(options.Value.EffectiveCatalogFile);
        _path = Path.Combine(Path.GetDirectoryName(catalog)!, Path.GetFileNameWithoutExtension(catalog) + ".pending.json");
    }

    /// <summary>The waiting changes, oldest first; an unreadable file is an empty list (nothing waits that cannot be read).</summary>
    public async Task<IReadOnlyList<PendingChange>> ListAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var text = await File.ReadAllTextAsync(_path, Encoding.UTF8, cancellationToken);
            return JsonSerializer.Deserialize<List<PendingChange>>(text, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Adds a change; false when the store is full. A newer change to the same item replaces the older one.</summary>
    public async Task<bool> AddAsync(PendingChange change, CancellationToken cancellationToken)
    {
        var all = (await ListAsync(cancellationToken))
            .Where(p => !(p.Section == change.Section && p.ItemId == change.ItemId && p.Channel == change.Channel && p.User == change.User))
            .ToList();
        if (all.Count >= Capacity)
        {
            return false;
        }

        all.Add(change);
        await SaveAsync(all, cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        var all = (await ListAsync(cancellationToken)).ToList();
        if (all.RemoveAll(p => p.Id == id) == 0)
        {
            return false;
        }

        await SaveAsync(all, cancellationToken);
        return true;
    }

    private async Task SaveAsync(IReadOnlyList<PendingChange> all, CancellationToken cancellationToken)
    {
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(all, Json), new UTF8Encoding(false), cancellationToken);
        File.Move(temporary, _path, overwrite: true);
    }
}
