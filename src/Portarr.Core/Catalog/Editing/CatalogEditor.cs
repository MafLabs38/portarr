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
}

public sealed partial class CatalogEditor : ICatalogEditor, IDisposable
{
    private const string BackupMarker = ".bak-";

    private readonly string _filePath;
    private readonly CatalogOptions _options;
    private readonly ICatalogService _catalogService;
    private readonly ICatalogParser _parser;
    private readonly ICatalogAuditLog _auditLog;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogEditor> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lazy<bool> _folderWritable;

    public CatalogEditor(
        IOptions<PortarrOptions> options,
        ICatalogService catalogService,
        ICatalogParser parser,
        ICatalogAuditLog auditLog,
        TimeProvider timeProvider,
        ILogger<CatalogEditor> logger)
    {
        _filePath = Path.GetFullPath(options.Value.EffectiveCatalogFile);
        _options = options.Value.Catalog;
        _catalogService = catalogService;
        _parser = parser;
        _auditLog = auditLog;
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
        return item is null ? null : new CatalogItem(CatalogVersion.Of(text), section, id, ProtectedSettings.Redact(item, ProtectedSettings.KeysOf(section, _parser.ConnectorSections)));
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
        if (!Writable)
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
        try
        {
            current = FindItem(text, edit.Section, edit.Id);
            next = ProtectedSettings.Apply(
                edit.Section,
                edit.Id,
                current,
                edit.Change(current?.DeepClone().AsObject()),
                ProtectedSettings.KeysOf(edit.Section, _parser.ConnectorSections));
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

        await WriteAsync(result, bom, cancellationToken);
        var after = CatalogVersion.Of(result);
        var changes = Describe(current, next);
        try
        {
            await _auditLog.AppendAsync(
                new CatalogAuditEntry(_timeProvider.GetUtcNow(), edit.Actor.Channel, edit.Actor.Client, edit.Operation, edit.Section, edit.Id, version, after, changes, edit.Actor.Via, edit.Actor.User),
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The file is written: the edit is applied. Say so, and that the trail missed it, instead of answering 500 for a change that happened.
            LogAuditFailed(edit.Section, edit.Id, exception.GetType().Name);
        }

        LogEdited(edit.Operation, edit.Section, edit.Id, edit.Actor.Channel, string.Join("; ", changes));

        // Our own write: reload now so the next read sees it; the watcher's later reload finds the same version.
        await _catalogService.ReloadAsync(cancellationToken);
        return new CatalogEditResult(CatalogEditStatus.Applied, after, changes, []);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Catalog edit of {Section}/{Id} was applied but the audit trail could not be written ({Error})")]
    private partial void LogAuditFailed(string section, string id, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog edited: {Operation} {Section}/{Id} through {Channel} ({Changes})")]
    private partial void LogEdited(string operation, string section, string id, string channel, string changes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog could not be replaced atomically, rewritten in place: {Reason}")]
    private partial void LogRenameFailed(string reason);
}
