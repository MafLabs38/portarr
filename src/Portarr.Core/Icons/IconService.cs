using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Status;

namespace Portarr.Core.Icons;

public sealed record IconFile(string Path, string ContentType);

public interface IIconService
{
    /// <summary>The icon file for a name, or null when no source has it.</summary>
    Task<IconFile?> GetAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves icons (D-47): the operator's <c>icons/</c> folder first, then the community set,
/// downloaded once and cached in the data folder. Only names used by the catalog are downloaded,
/// so anonymous requests cannot make Portarr fetch or store arbitrary files. Failures are
/// remembered in memory: a missing icon for a day, an unreachable source for a few minutes.
/// </summary>
public sealed partial class IconService : IIconService
{
    public const string HttpClientName = "icons";

    /// <summary>Community icons are small; anything bigger is not an icon.</summary>
    public const long MaxIconBytes = 1024 * 1024;

    private const int MaxNameLength = 64;
    private static readonly TimeSpan MissingRetryDelay = TimeSpan.FromDays(1);
    private static readonly TimeSpan OfflineRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly (string Extension, string ContentType)[] Formats =
        [("svg", "image/svg+xml"), ("png", "image/png"), ("webp", "image/webp")];

    private readonly string _customFolder;
    private readonly string _cacheFolder;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFleetService _fleetService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IconService> _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _retryAfter = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public IconService(
        IOptions<PortarrOptions> options,
        IHttpClientFactory httpClientFactory,
        IFleetService fleetService,
        TimeProvider timeProvider,
        ILogger<IconService> logger)
    {
        _customFolder = options.Value.CustomIconsFolder;
        _cacheFolder = Path.Combine(options.Value.DataFolder, "cache", "icons");
        _httpClientFactory = httpClientFactory;
        _fleetService = fleetService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IconFile?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        if (name.Length > MaxNameLength || !IconNames.IsValid(name))
        {
            return null;
        }

        var local = Find(_customFolder, name) ?? Find(_cacheFolder, name);
        if (local is not null)
        {
            return local;
        }

        if (!IsUsedByTheCatalog(name) || _retryAfter.TryGetValue(name, out var retryAfter) && _timeProvider.GetUtcNow() < retryAfter)
        {
            return null;
        }

        // One download per name at a time; different names download in parallel.
        var nameLock = _locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
        await nameLock.WaitAsync(cancellationToken);
        try
        {
            return Find(_cacheFolder, name) ?? await DownloadAsync(name, cancellationToken);
        }
        finally
        {
            nameLock.Release();
        }
    }

    private static IconFile? Find(string folder, string name)
    {
        foreach (var (extension, contentType) in Formats)
        {
            var path = Path.Combine(folder, $"{name}.{extension}");
            if (File.Exists(path))
            {
                return new IconFile(path, contentType);
            }
        }

        return null;
    }

    private bool IsUsedByTheCatalog(string name)
    {
        return _fleetService.GetApps().Any(app => app.Icon == name);
    }

    private async Task<IconFile?> DownloadAsync(string name, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            foreach (var (extension, contentType) in Formats.Take(2))
            {
                using var response = await client.GetAsync($"{extension}/{name}.{extension}", cancellationToken);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxIconBytes)
                {
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                Directory.CreateDirectory(_cacheFolder);
                var path = Path.Combine(_cacheFolder, $"{name}.{extension}");
                var temporary = path + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                File.Move(temporary, path, overwrite: true);
                LogCached(name);

                return new IconFile(path, contentType);
            }

            _retryAfter[name] = _timeProvider.GetUtcNow() + MissingRetryDelay;
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Offline, blocked or too slow: try again later, not on every page load.
            _retryAfter[name] = _timeProvider.GetUtcNow() + OfflineRetryDelay;
            LogUnavailable(name, exception.Message);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Icon {Name} downloaded and cached")]
    private partial void LogCached(string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Icon source unavailable for {Name}: {Reason}")]
    private partial void LogUnavailable(string name, string reason);
}
