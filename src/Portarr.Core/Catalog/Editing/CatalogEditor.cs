using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Portarr.Core.Catalog.Editing;

/// <summary>
/// Edits the catalog file (D-83): the file stays the source of truth (D-44), the API and the MCP write tools change
/// it through this one path. Every edit is checked against the version the caller read, applied at the text level
/// (comments and formatting kept, <see cref="CatalogTextEditor"/>), validated by the catalog parser on the whole
/// resulting file, then written atomically (temporary file and rename) after keeping the previous version; it is
/// audited and the catalog reloaded. Edits are serialized. A refused edit leaves the file untouched.
/// </summary>
public interface ICatalogEditor
{
    bool Writable { get; }

    /// <summary>The version of the file as it is now.</summary>
    Task<string> GetVersionAsync(CancellationToken cancellationToken);

    /// <summary>One item as the file holds it, or null.</summary>
    Task<CatalogItem?> GetItemAsync(string section, string id, CancellationToken cancellationToken);

    /// <summary>The ids of a section as the file holds them (also when the file does not load).</summary>
    Task<IReadOnlyList<string>> GetIdsAsync(string section, CancellationToken cancellationToken);

    Task<CatalogEditResult> EditAsync(CatalogEdit edit, CancellationToken cancellationToken);

    /// <summary>The items of a section as the file holds them, how hosts are reached included only when <paramref name="withConnections"/> (a person who may reconfigure), with the version to send back.</summary>
    Task<(string Version, IReadOnlyList<JsonObject> Items)> GetItemsAsync(string section, bool withConnections, CancellationToken cancellationToken);

    /// <summary>The catalog file as it is, to download or commit back.</summary>
    Task<(string Version, string Text)> ExportAsync(CancellationToken cancellationToken);

    /// <summary>The changes waiting for an administrator's approval, oldest first; how hosts are reached is left out of what they show unless <paramref name="withConnections"/>.</summary>
    Task<IReadOnlyList<PendingChange>> ListPendingAsync(bool withConnections, CancellationToken cancellationToken);

    /// <summary>Applies a waiting change as <paramref name="approver"/> (who may reconfigure); refused when the item changed since it was asked for.</summary>
    Task<CatalogEditResult> ApproveAsync(string pendingId, CatalogActor approver, CancellationToken cancellationToken);

    /// <summary>Drops a waiting change without applying it; false when there is none with this id.</summary>
    Task<bool> RejectAsync(string pendingId, CatalogActor rejecter, CancellationToken cancellationToken);
}

public sealed partial class CatalogEditor : ICatalogEditor, IDisposable
{
    private const string BackupMarker = ".bak-";

    private readonly string _filePath;
    private readonly CatalogOptions _options;
    private readonly ICatalogService _catalogService;
    private readonly ICatalogParser _parser;
    private readonly ICatalogAuditLog _auditLog;
    private readonly CatalogPendingStore _pending;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogEditor> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lazy<bool> _folderWritable;

    public CatalogEditor(
        IOptions<PortarrOptions> options,
        ICatalogService catalogService,
        ICatalogParser parser,
        ICatalogAuditLog auditLog,
        CatalogPendingStore pending,
        TimeProvider timeProvider,
        ILogger<CatalogEditor> logger)
    {
        _filePath = Path.GetFullPath(options.Value.EffectiveCatalogFile);
        _options = options.Value.Catalog;
        _catalogService = catalogService;
        _parser = parser;
        _auditLog = auditLog;
        _pending = pending;
        _timeProvider = timeProvider;
        _logger = logger;
        _folderWritable = new Lazy<bool>(FolderIsWritable);
    }

    /// <summary>Writes are on and the catalog's folder accepts them (checked once): a read-only mount is "read-only", not a 500 on the first edit.</summary>
    public bool Writable => _options.Writable && _folderWritable.Value;

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        return CatalogVersion.Of((await ReadFileAsync(cancellationToken)).Text);
    }

    public async Task<CatalogItem?> GetItemAsync(string section, string id, CancellationToken cancellationToken)
    {
        var (text, _) = await ReadFileAsync(cancellationToken);
        var item = FindItem(text, section, id);

        // What an assistant reads leaves out how hosts are reached (addresses, credential variables); an edit that omits them keeps them.
        return item is null ? null : new CatalogItem(CatalogVersion.Of(text), section, id, ProtectedSettings.Redact(item, ProtectedSettings.KeysOf(section, _parser.GetConnectorSections())));
    }

    public async Task<IReadOnlyList<string>> GetIdsAsync(string section, CancellationToken cancellationToken)
    {
        var (text, _) = await ReadFileAsync(cancellationToken);
        return Section(text, section)?.Children.OfType<YamlMappingNode>()
            .Select(m => m.Children.FirstOrDefault(c => c.Key is YamlScalarNode { Value: "id" }).Value as YamlScalarNode)
            .Select(v => v?.Value)
            .OfType<string>()
            .ToList() ?? [];
    }

    public async Task<CatalogEditResult> EditAsync(CatalogEdit edit, CancellationToken cancellationToken)
    {
        if (!Writable && !edit.DryRun)
        {
            return Refused(
                CatalogEditStatus.ReadOnly,
                string.Empty,
                _options.Writable
                    ? "The catalog is read-only: its folder is not writable. Mount the folder (not the single file) read-write, or turn PORTARR__CATALOG__WRITABLE off."
                    : "The catalog is read-only: set PORTARR__CATALOG__WRITABLE=true to edit it through Portarr.");
        }

        if (!CatalogSections.All.Contains(edit.Section) && edit.Section != CatalogSections.Portal)
        {
            return Refused(CatalogEditStatus.Invalid, string.Empty, $"section must be one of {string.Join(", ", CatalogSections.All)}.");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            return await EditLockedAsync(edit, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose() => _writeLock.Dispose();

    private async Task<CatalogEditResult> EditLockedAsync(CatalogEdit edit, CancellationToken cancellationToken)
    {
        var (text, bom) = await ReadFileAsync(cancellationToken);
        var version = CatalogVersion.Of(text);
        if (string.IsNullOrWhiteSpace(edit.ExpectedVersion))
        {
            return Refused(CatalogEditStatus.Invalid, version, "version is required: send the version you read (GET /api/v1/catalog/status, or the item).");
        }

        if (!string.Equals(edit.ExpectedVersion.Trim(), version, StringComparison.OrdinalIgnoreCase))
        {
            return Refused(CatalogEditStatus.Conflict, version, $"The catalog changed since version {edit.ExpectedVersion.Trim()} (now {version}): read it again and retry.");
        }

        string result;
        JsonObject? current;
        JsonObject? next;
        bool needsApproval;
        try
        {
            current = FindItem(text, edit.Section, edit.Id);
            var keys = ProtectedSettings.KeysOf(edit.Section, _parser.GetConnectorSections());
            var proposed = edit.Change(current?.DeepClone().AsObject());
            var touches = ProtectedSettings.Touches(current, proposed, keys);
            needsApproval = touches && !edit.Actor.Reconfigure;
            next = ProtectedSettings.WithoutNulls(ProtectedSettings.Apply(edit.Section, edit.Id, current, proposed, keys, allowChange: true));
            var guarded = ProtectedSettings.GuardedKeysOf(edit.Section, _parser.GetConnectorSections());
            if (ProtectedSettings.ChangedKeys(current, next, guarded) is { Count: > 0 } changedGuarded && ConnectionGuardrails.Check(next, changedGuarded) is { Count: > 0 } problems)
            {
                return new CatalogEditResult(CatalogEditStatus.Invalid, version, [], problems);
            }

            if (YamlNodes.Canonical(current) == YamlNodes.Canonical(next))
            {
                return new CatalogEditResult(CatalogEditStatus.Unchanged, version, [], []);
            }

            result = (edit.Section == CatalogSections.Portal
                ? CatalogTextEditor.ApplySetting(text, edit.Section, next)
                : CatalogTextEditor.Apply(text, edit.Section, edit.Id, next)).Text;
        }
        catch (CatalogEditException exception)
        {
            return Refused(exception.Status, version, exception.Message);
        }
        catch (YamlException exception)
        {
            return Refused(CatalogEditStatus.Invalid, version, $"The catalog file is not valid YAML (line {exception.Start.Line}): fix it by hand first.");
        }

        var parsed = _parser.Parse(result);
        if (!parsed.IsValid)
        {
            return new CatalogEditResult(CatalogEditStatus.Invalid, version, [], parsed.Errors);
        }

        var changes = Describe(current, next);
        if (needsApproval)
        {
            return await ProposeAsync(edit, version, current, next, changes, cancellationToken);
        }

        if (edit.DryRun)
        {
            return new CatalogEditResult(CatalogEditStatus.Valid, version, changes, []);
        }

        await WriteAsync(result, bom, cancellationToken);
        var after = CatalogVersion.Of(result);
        await AuditAsync(new CatalogAuditEntry(_timeProvider.GetUtcNow(), edit.Actor.Channel, edit.Actor.Client, edit.Operation, edit.Section, edit.Id, version, after, changes, edit.Actor.Via, edit.Actor.User), cancellationToken);
        LogEdited(edit.Operation, edit.Section, edit.Id, edit.Actor.Channel, string.Join("; ", changes));

        // Our own write: reload now so the next read sees it; the watcher's later reload finds the same version.
        await _catalogService.ReloadAsync(cancellationToken);
        return new CatalogEditResult(CatalogEditStatus.Applied, after, changes, []);
    }

    /// <summary>The edit changes how a host is reached and its author may not do that alone: it is kept for an administrator, nothing is written.</summary>
    private async Task<CatalogEditResult> ProposeAsync(CatalogEdit edit, string version, JsonObject? current, JsonObject? next, List<string> changes, CancellationToken cancellationToken)
    {
        if (edit.DryRun)
        {
            return new CatalogEditResult(CatalogEditStatus.Pending, version, changes, []);
        }

        var replaced = (await _pending.ListAsync(cancellationToken)).Where(p => CatalogPendingStore.Replaces(p, edit.Section, edit.Id, edit.Actor.Channel, edit.Actor.User)).ToList();
        var pending = new PendingChange(Guid.NewGuid().ToString("N")[..12], _timeProvider.GetUtcNow(), edit.Actor.Channel, edit.Actor.User, edit.Actor.Via, edit.Section, edit.Id, edit.Operation, current, next, changes);
        bool added;
        try
        {
            added = await _pending.AddAsync(pending, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogPendingNotUpdated(edit.Section, edit.Id, exception.GetType().Name);
            return Refused(CatalogEditStatus.Invalid, version, "The waiting changes could not be saved (is the catalog folder writable?): nothing was kept.");
        }

        if (!added)
        {
            return Refused(CatalogEditStatus.Invalid, version, $"Too many changes already wait for approval ({CatalogPendingStore.Capacity} in all, {CatalogPendingStore.CapacityPerAuthor} from one author): approve or reject some in Settings, Catalog.");
        }

        foreach (var old in replaced)
        {
            await AuditAsync(new CatalogAuditEntry(pending.At, edit.Actor.Channel, edit.Actor.Client, $"supersede:{old.Operation}", old.Section, old.ItemId, version, version, old.Changes, $"superseded:{old.Id} by {pending.Id}", edit.Actor.User), cancellationToken);
        }

        await AuditAsync(new CatalogAuditEntry(pending.At, edit.Actor.Channel, edit.Actor.Client, $"propose:{edit.Operation}", edit.Section, edit.Id, version, version, changes, edit.Actor.Via, edit.Actor.User), cancellationToken);
        LogEdited($"propose:{edit.Operation}", edit.Section, edit.Id, edit.Actor.Channel, string.Join("; ", changes));
        return new CatalogEditResult(CatalogEditStatus.Pending, version, changes, [], pending.Id);
    }

    private async Task AuditAsync(CatalogAuditEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _auditLog.AppendAsync(entry, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The action happened (a write, a proposal, a rejection): say that its trail missed it, instead of answering 500 for it.
            LogAuditFailed(entry.Section, entry.Id, exception.GetType().Name);
        }
    }

    public async Task<(string Version, IReadOnlyList<JsonObject> Items)> GetItemsAsync(string section, bool withConnections, CancellationToken cancellationToken)
    {
        var (text, _) = await ReadFileAsync(cancellationToken);
        var keys = withConnections ? [] : ProtectedSettings.KeysOf(section, _parser.GetConnectorSections());
        var items = (Section(text, section)?.Children.OfType<YamlMappingNode>() ?? [])
            .Select(node => ProtectedSettings.Redact((JsonObject)YamlNodes.ToJson(node)!, keys))
            .ToList();
        return (CatalogVersion.Of(text), items);
    }

    public async Task<(string Version, string Text)> ExportAsync(CancellationToken cancellationToken)
    {
        var (text, _) = await ReadFileAsync(cancellationToken);
        return (CatalogVersion.Of(text), text);
    }

    public async Task<IReadOnlyList<PendingChange>> ListPendingAsync(bool withConnections, CancellationToken cancellationToken)
    {
        var all = await _pending.ListAsync(cancellationToken);
        if (withConnections)
        {
            return all;
        }

        // The same rule as a read of the item: how a host is reached is for whoever may reconfigure (a diff would show it).
        var connectors = _parser.GetConnectorSections();
        return [.. all.Select(p =>
        {
            var keys = ProtectedSettings.KeysOf(p.Section, connectors);
            var changes = p.Changes.Where(line => !keys.Any(key => line.StartsWith(key + ":", StringComparison.Ordinal))).ToList();
            return p with
            {
                Before = p.Before is null ? null : ProtectedSettings.Redact(p.Before, keys),
                After = p.After is null ? null : ProtectedSettings.Redact(p.After, keys),
                Changes = changes.Count == p.Changes.Count ? changes : [.. changes, "connection settings changed (an administrator sees them)"],
            };
        })];
    }

    public async Task<CatalogEditResult> ApproveAsync(string pendingId, CatalogActor approver, CancellationToken cancellationToken)
    {
        if (!Writable)
        {
            return Refused(CatalogEditStatus.ReadOnly, string.Empty, "The catalog is read-only: set PORTARR__CATALOG__WRITABLE=true to edit it through Portarr.");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var change = (await _pending.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == pendingId);
            if (change is null)
            {
                return Refused(CatalogEditStatus.NotFound, string.Empty, $"No change waits with id '{pendingId}'.");
            }

            var (text, _) = await ReadFileAsync(cancellationToken);
            var version = CatalogVersion.Of(text);
            var actor = approver with { Reconfigure = true, Via = $"approved:{change.Id} asked by {change.User ?? change.Channel}{(change.Via is null ? string.Empty : $" via {change.Via}")}" };
            var result = await EditLockedAsync(
                new CatalogEdit(change.Section, change.ItemId, $"approve:{change.Operation}", version, actor, now => PendingMerge.Apply(change.Section, change.ItemId, change.Before, change.After, now)),
                cancellationToken);
            if (result.Succeeded)
            {
                try
                {
                    await _pending.RemoveAsync(change.Id, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The catalog is written: the change is applied. Answer so; the entry that stays in the queue answers unchanged and clears if approved again.
                    LogPendingNotUpdated(change.Section, change.ItemId, exception.GetType().Name);
                }
            }

            return result;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> RejectAsync(string pendingId, CatalogActor rejecter, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var change = (await _pending.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == pendingId);
            if (change is null)
            {
                return false;
            }

            var version = CatalogVersion.Of((await ReadFileAsync(cancellationToken)).Text);
            if (!await _pending.RemoveAsync(pendingId, cancellationToken))
            {
                return false;
            }

            await AuditAsync(new CatalogAuditEntry(_timeProvider.GetUtcNow(), rejecter.Channel, rejecter.Client, $"reject:{change.Operation}", change.Section, change.ItemId, version, version, change.Changes, $"rejected:{change.Id} asked by {change.User ?? change.Channel}", rejecter.User), cancellationToken);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Whether a file can be created next to the catalog: backups, the audit trail and the atomic replace all live there.</summary>
    private bool FolderIsWritable()
    {
        if (!_options.Writable)
        {
            return true;
        }

        var folder = Path.GetDirectoryName(_filePath)!;
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".portarr-{Guid.NewGuid():N}.probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFolderNotWritable(folder, exception.GetType().Name);
            return false;
        }
    }

    private static CatalogEditResult Refused(CatalogEditStatus status, string version, string error) => new(status, version, [], [error]);

    /// <summary>What changed, key by key: <c>added</c>, <c>removed</c> or <c>notes: changed</c>.</summary>
    private static List<string> Describe(JsonObject? before, JsonObject? after)
    {
        if (before is null)
        {
            return ["added"];
        }

        if (after is null)
        {
            return ["removed"];
        }

        var keys = before.Select(p => p.Key).Union(after.Select(p => p.Key), StringComparer.Ordinal);
        return keys.Select(key => (before[key], after[key]) switch
            {
                (null, null) => null,
                (null, _) => $"{key}: added",
                (_, null) => $"{key}: removed",
                var (b, a) when YamlNodes.Canonical(b) != YamlNodes.Canonical(a) => $"{key}: changed",
                _ => null,
            })
            .OfType<string>()
            .ToList();
    }

    private static YamlSequenceNode? Section(string text, string section)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(text);
        stream.Load(reader);
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return null;
        }

        return root.Children.FirstOrDefault(p => p.Key is YamlScalarNode { } key && key.Value == section).Value as YamlSequenceNode;
    }

    private static JsonObject? FindItem(string text, string section, string id)
    {
        if (section == CatalogSections.Portal)
        {
            return Setting(text, section);
        }

        var item = Section(text, section)?.Children.OfType<YamlMappingNode>()
            .FirstOrDefault(m => m.Children.Any(c => c.Key is YamlScalarNode { Value: "id" } && c.Value is YamlScalarNode v && v.Value == id));
        return item is null ? null : (JsonObject)YamlNodes.ToJson(item)!;
    }

    /// <summary>A top-level mapping that is not a list (<c>portal:</c>) as JSON, or null.</summary>
    private static JsonObject? Setting(string text, string key)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(text);
        stream.Load(reader);
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return null;
        }

        var node = root.Children.FirstOrDefault(p => p.Key is YamlScalarNode { } k && k.Value == key).Value;
        return node is YamlMappingNode ? YamlNodes.ToJson(node) as JsonObject : null;
    }

    private async Task<(string Text, bool Bom)> ReadFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return (string.Empty, false);
        }

        var bytes = await File.ReadAllBytesAsync(_filePath, cancellationToken);
        var bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        return (Encoding.UTF8.GetString(bom ? bytes[3..] : bytes), bom);
    }

    /// <summary>
    /// Temporary file in the same folder, flushed to disk, previous version kept, then renamed over the catalog (an
    /// atomic replace). A catalog bind-mounted as a single file cannot be replaced by a rename: it is then
    /// rewritten in place.
    /// </summary>
    private async Task WriteAsync(string text, bool bom, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(_filePath)!;
        var name = Path.GetFileName(_filePath);
        Directory.CreateDirectory(folder);

        var bytes = (bom ? Encoding.UTF8.Preamble.ToArray() : []).Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var temp = Path.Combine(folder, $".{name}.{Guid.NewGuid():N}.tmp");
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(bytes, cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        try
        {
            if (File.Exists(_filePath))
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(temp, File.GetUnixFileMode(_filePath));
                }

                KeepPreviousVersion(folder, name);
            }

            try
            {
                File.Move(temp, _filePath, overwrite: true);
            }
            catch (IOException exception)
            {
                LogRenameFailed(exception.Message);
                await File.WriteAllBytesAsync(_filePath, bytes, cancellationToken);
            }
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private void KeepPreviousVersion(string folder, string name)
    {
        if (_options.Backups <= 0)
        {
            return;
        }

        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var backup = Path.Combine(folder, name + BackupMarker + stamp);
        var attempt = 1;
        while (File.Exists(backup))
        {
            backup = Path.Combine(folder, $"{name}{BackupMarker}{stamp}-{attempt}");
            attempt++;
        }

        File.Copy(_filePath, backup);

        // Timestamps sort as text: the oldest beyond the limit go.
        foreach (var old in Directory.GetFiles(folder, name + BackupMarker + "*").OrderDescending(StringComparer.Ordinal).Skip(_options.Backups))
        {
            File.Delete(old);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "PORTARR__CATALOG__WRITABLE is on but the catalog folder {Folder} is not writable ({Error}): the catalog stays read-only. Mount the folder, not the single file, read-write")]
    private partial void LogFolderNotWritable(string folder, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Catalog action on {Section}/{Id} happened but the audit trail could not be written ({Error})")]
    private partial void LogAuditFailed(string section, string id, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Catalog change {Section}/{Id}: the waiting list could not be updated ({Error})")]
    private partial void LogPendingNotUpdated(string section, string id, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog edited: {Operation} {Section}/{Id} through {Channel} ({Changes})")]
    private partial void LogEdited(string operation, string section, string id, string channel, string changes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog could not be replaced atomically, rewritten in place: {Reason}")]
    private partial void LogRenameFailed(string reason);
}
