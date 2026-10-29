using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;

namespace Portarr.Core.Icons;

public sealed record IconFile(string Path, string ContentType);

public interface IIconService
{
    /// <summary>The icon file for a name, or null when no source has it.</summary>
    Task<IconFile?> GetAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves icons (D-47): the operator's <c>icons/</c> folder first, then the community set,
/// downloaded once and cached in the data folder. A name missing everywhere is remembered for a
/// day so the community site is not asked again on every page load.
/// </summary>
public sealed partial class IconService : IIconService
{
    public const string HttpClientName = "icons";

    private static readonly TimeSpan MissingRetryDelay = TimeSpan.FromDays(1);
    private static readonly (string Extension, string ContentType)[] Formats =
        [("svg", "image/svg+xml"), ("png", "image/png"), ("webp", "image/webp")];

    private readonly string _customFolder;
    private readonly string _cacheFolder;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IconService> _logger;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public IconService(IOptions<PortarrOptions> options, IHttpClientFactory httpClientFactory, TimeProvider timeProvider, ILogger<IconService> logger)
    {
        _customFolder = options.Value.CustomIconsFolder;
        _cacheFolder = Path.Combine(options.Value.DataFolder, "cache", "icons");
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IconFile?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!IconNames.IsValid(name))
        {
            return null;
        }

        var local = Find(_customFolder, name) ?? Find(_cacheFolder, name);
        if (local is not null)
        {
            return local;
        }

        await _downloadLock.WaitAsync(cancellationToken);
        try
        {
            // Another request may have downloaded it while this one waited.
            return Find(_cacheFolder, name) ?? (IsKnownMissing(name) ? null : await DownloadAsync(name, cancellationToken));
        }
        finally
        {
            _downloadLock.Release();
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

    private bool IsKnownMissing(string name)
    {
        var marker = MissingMarker(name);
        return File.Exists(marker) && _timeProvider.GetUtcNow() - File.GetLastWriteTimeUtc(marker) < MissingRetryDelay;
    }

    private async Task<IconFile?> DownloadAsync(string name, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            foreach (var (extension, contentType) in Formats.Take(2))
            {
                using var response = await client.GetAsync($"{extension}/{name}.{extension}", cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                Directory.CreateDirectory(_cacheFolder);
                var path = Path.Combine(_cacheFolder, $"{name}.{extension}");
                var temporary = path + ".tmp";
                await File.WriteAllBytesAsync(temporary, await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
                File.Move(temporary, path, overwrite: true);
                LogCached(name);

                return new IconFile(path, contentType);
            }

            Directory.CreateDirectory(_cacheFolder);
            await File.WriteAllTextAsync(MissingMarker(name), string.Empty, cancellationToken);
            return null;
        }
        catch (HttpRequestException exception)
        {
            // Offline or blocked: no marker, the next request tries again.
            LogUnavailable(name, exception.Message);
            return null;
        }
    }

    private string MissingMarker(string name)
    {
        return Path.Combine(_cacheFolder, $"{name}.missing");
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Icon {Name} downloaded and cached")]
    private partial void LogCached(string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Icon source unavailable for {Name}: {Reason}")]
    private partial void LogUnavailable(string name, string reason);
}
