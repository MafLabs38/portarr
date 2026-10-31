using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Common;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;
using Portarr.Core.Configuration;

namespace Portarr.Core.Portal;

/// <summary>A logo that passed the checks: where it is, how it is served and a hash of its bytes (cache busting, ETag).</summary>
public sealed record PortalLogoFile(string Path, string ContentType, string Hash);

/// <summary>
/// The identity as the portal shows it. <c>Logo</c> is the path the catalog declares; <c>LogoFile</c> is the file when
/// it can be served, null when none is declared (Portarr's own logo is used) or when <c>LogoProblem</c> says why not.
/// <c>Colors</c> are those of the badge texts, Portarr's accent where the catalog says nothing.
/// </summary>
public sealed record PortalIdentity(string Name, string? Logo, PortalLogoFile? LogoFile, string? LogoProblem, PortalColors Colors);

public interface IPortalService
{
    /// <summary>The identity declared in the catalog, with the defaults (Portarr's own name and logo) where it says nothing.</summary>
    PortalIdentity Get();

    /// <summary>The logos already in the branding folder, as catalog paths (<c>branding/logo.png</c>).</summary>
    IReadOnlyList<string> ListBranding();

    /// <summary>
    /// Stores an uploaded logo in the branding folder (atomically, the previous file of that name kept), audits it and
    /// sets <c>portal.logo</c> through the catalog editor.
    /// </summary>
    Task<PortalLogoUpload> SaveLogoAsync(string fileName, ReadOnlyMemory<byte> content, CatalogActor actor, CancellationToken cancellationToken);
}

/// <summary>The outcome of an upload: <c>Edit</c> says what happened to the catalog, <c>Path</c> is the stored file when it was written.</summary>
public sealed record PortalLogoUpload(CatalogEditResult Edit, string? Path);

/// <summary>
/// Resolves and stores the portal's identity (D-91). The name and the logo path come from the catalog; the logo file
/// is read from the catalog folder only (never outside it), checked once per change of the file (size, signature,
/// svg content) and then served from the cache. A logo that fails the checks falls back to Portarr's own.
/// </summary>
public sealed partial class PortalService : IPortalService
{
    private const int MaxListed = 100;
    private const int KeptCopies = 3;

    private readonly string _catalogFolder;
    private readonly string _brandingFolder;
    private readonly ICatalogService _catalogService;
    private readonly ICatalogEditor _editor;
    private readonly ICatalogAuditLog _auditLog;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PortalService> _logger;
    private readonly object _cacheLock = new();
    private (string Path, DateTime Modified, long Length, PortalLogoFile? File, string? Problem)? _cache;

    public PortalService(
        IOptions<PortarrOptions> options,
        ICatalogService catalogService,
        ICatalogEditor editor,
        ICatalogAuditLog auditLog,
        TimeProvider timeProvider,
        ILogger<PortalService> logger)
    {
        _catalogFolder = Path.GetDirectoryName(Path.GetFullPath(options.Value.EffectiveCatalogFile))!;
        _brandingFolder = options.Value.BrandingFolder;
        _catalogService = catalogService;
        _editor = editor;
        _auditLog = auditLog;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public PortalIdentity Get()
    {
        var portal = _catalogService.Current.Catalog.Portal;
        var name = portal?.Name ?? BuildInfo.AppName;
        var colors = new PortalColors(portal?.Colors?.Name ?? PortalColor.Default, portal?.Colors?.Version ?? PortalColor.Default);
        if (portal?.Logo is not { } logo)
        {
            return new PortalIdentity(name, null, null, null, colors);
        }

        var (file, problem) = Load(logo);
        return new PortalIdentity(name, logo, file, problem, colors);
    }

    public IReadOnlyList<string> ListBranding()
    {
        if (!Directory.Exists(_brandingFolder))
        {
            return [];
        }

        return Directory.EnumerateFiles(_brandingFolder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => PortalLogo.ValidatePath($"{PortalLogo.Folder}/{name}") is null)
            .Order(StringComparer.Ordinal)
            .Take(MaxListed)
            .Select(name => $"{PortalLogo.Folder}/{name}")
            .Where(path => PortalLogo.Resolve(_catalogFolder, path) is not null)
            .ToList();
    }

    public async Task<PortalLogoUpload> SaveLogoAsync(string fileName, ReadOnlyMemory<byte> content, CatalogActor actor, CancellationToken cancellationToken)
    {
        var version = await _editor.GetVersionAsync(cancellationToken);
        if (!_editor.Writable)
        {
            return new PortalLogoUpload(new CatalogEditResult(CatalogEditStatus.ReadOnly, version, [], ["The catalog is read-only: set PORTARR__CATALOG__WRITABLE=true to edit it through Portarr."]), null);
        }

        var (format, error) = PortalLogo.Inspect(content.Span, Path.GetExtension(fileName));
        if (format is null)
        {
            return new PortalLogoUpload(new CatalogEditResult(CatalogEditStatus.Invalid, version, [], [error!]), null);
        }

        var relative = $"{PortalLogo.Folder}/{StoredName(fileName)}.{format.Extension}";
        await WriteAtomicallyAsync(Path.Combine(_catalogFolder, relative), content, cancellationToken);
        LogStored(relative, content.Length, actor.Channel);

        var edit = await _editor.EditAsync(
            new CatalogEdit(CatalogSections.Portal, CatalogSections.Portal, "set-logo", version, actor, CatalogEdits.SetPortal(null, relative)),
            cancellationToken);
        if (edit.Succeeded)
        {
            // The file may have replaced one of the same name while the catalog stays as it was: the upload is audited by itself.
            await _auditLog.AppendAsync(
                new CatalogAuditEntry(_timeProvider.GetUtcNow(), actor.Channel, actor.Client, "upload-logo", CatalogSections.Portal, CatalogSections.Portal, version, edit.Version, [$"logo: {relative} written ({content.Length} bytes)"], actor.Via, actor.User),
                cancellationToken);
        }

        return new PortalLogoUpload(edit, relative);
    }

    /// <summary>A file name reduced to what a catalog path takes: lowercase letters, digits and dashes (<c>My Logo.PNG</c> becomes <c>my-logo</c>).</summary>
    private static string StoredName(string fileName)
    {
        var stem = new StringBuilder();
        foreach (var c in Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                stem.Append(c);
            }
            else if (stem.Length > 0 && stem[^1] != '-')
            {
                stem.Append('-');
            }
        }

        var name = stem.ToString().Trim('-');
        name = name.Length > PortalLogo.MaxNameLength ? name[..PortalLogo.MaxNameLength].TrimEnd('-') : name;
        return name.Length == 0 ? "logo" : name;
    }

    /// <summary>Temporary file, flushed to disk, previous file of that name kept next to it, then renamed over it.</summary>
    private async Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(content, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                KeepPreviousFile(path);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private void KeepPreviousFile(string path)
    {
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        File.Copy(path, $"{path}.bak-{stamp}", overwrite: true);

        // Timestamps sort as text: the oldest beyond the limit go.
        foreach (var old in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".bak-*").OrderDescending(StringComparer.Ordinal).Skip(KeptCopies))
        {
            File.Delete(old);
        }
    }

    private (PortalLogoFile? File, string? Problem) Load(string logo)
    {
        var full = PortalLogo.Resolve(_catalogFolder, logo);
        if (full is null)
        {
            return (null, $"{logo} is not a file inside the catalog folder.");
        }

        var info = new FileInfo(full);
        lock (_cacheLock)
        {
            if (_cache is { } cached && cached.Path == full && cached.Modified == info.LastWriteTimeUtc && cached.Length == info.Length)
            {
                return (cached.File, cached.Problem);
            }

            var (file, problem) = Inspect(full, info);
            _cache = (full, info.LastWriteTimeUtc, info.Length, file, problem);
            if (problem is not null)
            {
                LogRefused(logo, problem);
            }

            return (file, problem);
        }
    }

    private static (PortalLogoFile? File, string? Problem) Inspect(string full, FileInfo info)
    {
        if (info.Length > PortalLogo.MaxBytes)
        {
            return (null, $"The image is larger than {PortalLogo.MaxBytes / 1024} KiB.");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(full);
        }
        catch (IOException exception)
        {
            return (null, exception.Message);
        }

        var (format, error) = PortalLogo.Inspect(bytes, Path.GetExtension(full));
        return format is null
            ? (null, error)
            : (new PortalLogoFile(full, format.ContentType, Convert.ToHexStringLower(SHA256.HashData(bytes))[..12]), null);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Portal logo {Path} stored ({Bytes} bytes) through {Channel}")]
    private partial void LogStored(string path, int bytes, string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Portal logo {Path} is not used: {Reason}")]
    private partial void LogRefused(string path, string reason);
}
