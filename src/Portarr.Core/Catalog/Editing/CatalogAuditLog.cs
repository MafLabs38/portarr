using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;

namespace Portarr.Core.Catalog.Editing;

/// <summary>
/// One catalog edit as the audit log keeps it (D-85): when, through which channel and from which address, what
/// (operation, section, id), the versions before and after and a summary of the changes; <c>Via</c> names the
/// built-in chat conversation when the edit came from it (D-89). Never a key, never the values themselves.
/// </summary>
public sealed record CatalogAuditEntry(
    DateTimeOffset At,
    string Channel,
    string? Client,
    string Operation,
    string Section,
    string Id,
    string VersionBefore,
    string VersionAfter,
    IReadOnlyList<string> Changes,
    string? Via = null);

public interface ICatalogAuditLog
{
    Task AppendAsync(CatalogAuditEntry entry, CancellationToken cancellationToken);

    /// <summary>The latest entries, newest first.</summary>
    Task<IReadOnlyList<CatalogAuditEntry>> ReadAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>
/// An append-only JSON Lines file next to the catalog (<c>catalog.audit.jsonl</c>, D-85): the history of the file
/// lives with the file, whatever database Portarr uses, and is readable with any tool. Past 1 MiB it is rotated
/// once (<c>.1</c>): a trace of recent edits, not an archive (the operator's own versioning of the catalog is).
/// </summary>
public sealed class CatalogAuditLog : ICatalogAuditLog
{
    public const int MaxBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CatalogAuditLog(IOptions<PortarrOptions> options)
    {
        var catalog = Path.GetFullPath(options.Value.EffectiveCatalogFile);
        _path = Path.Combine(Path.GetDirectoryName(catalog)!, Path.GetFileNameWithoutExtension(catalog) + ".audit.jsonl");
    }

    public async Task AppendAsync(CatalogAuditEntry entry, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
            {
                File.Move(_path, _path + ".1", overwrite: true);
            }

            await File.AppendAllTextAsync(_path, JsonSerializer.Serialize(entry, Json) + "\n", Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<CatalogAuditEntry>> ReadAsync(int limit, CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(_path, cancellationToken);
        var result = new List<CatalogAuditEntry>();
        for (var i = lines.Length - 1; i >= 0 && result.Count < limit; i--)
        {
            try
            {
                if (lines[i].Length > 0 && JsonSerializer.Deserialize<CatalogAuditEntry>(lines[i], Json) is { } entry)
                {
                    result.Add(entry);
                }
            }
            catch (JsonException)
            {
                // A line edited by hand or cut by a crash: the others stay readable.
            }
        }

        return result;
    }
}
