using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
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
public sealed partial class CatalogPendingStore
{
    public const int Capacity = 50;

    /// <summary>What one author (a user on a channel) may leave waiting: one assistant cannot fill the queue for the others.</summary>
    public const int CapacityPerAuthor = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly TimeProvider _clock;

    public CatalogPendingStore(IOptions<PortarrOptions> options, ILogger<CatalogPendingStore>? logger = null, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CatalogPendingStore>.Instance;
        var catalog = Path.GetFullPath(options.Value.EffectiveCatalogFile);
        _path = Path.Combine(Path.GetDirectoryName(catalog)!, Path.GetFileNameWithoutExtension(catalog) + ".pending.json");
    }

    /// <summary>
    /// The waiting changes, oldest first. A file that cannot be read is an empty list, but it is moved aside (<c>.corrupt-…</c>) and
    /// logged first, so the next change does not silently overwrite what it held.
    /// </summary>
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
            var aside = $"{_path}.corrupt-{_clock.GetUtcNow():yyyyMMddHHmmss}";
            File.Move(_path, aside, overwrite: true);
            LogUnreadable(aside);
            return [];
        }
    }

    /// <summary>Whether <paramref name="existing"/> is the one a new change by this author to this item replaces.</summary>
    public static bool Replaces(PendingChange existing, string section, string itemId, string channel, string? user)
    {
        return existing.Section == section && existing.ItemId == itemId && existing.Channel == channel && existing.User == user;
    }

    /// <summary>Adds a change; false when the store (or the author's share of it) is full. A newer change to the same item by the same author replaces the older one.</summary>
    public async Task<bool> AddAsync(PendingChange change, CancellationToken cancellationToken)
    {
        var all = (await ListAsync(cancellationToken))
            .Where(p => !Replaces(p, change.Section, change.ItemId, change.Channel, change.User))
            .ToList();
        if (all.Count >= Capacity || all.Count(p => p.Channel == change.Channel && p.User == change.User) >= CapacityPerAuthor)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The pending changes file could not be read; moved aside to {Aside} and started again empty.")]
    private partial void LogUnreadable(string aside);
}
