using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;

namespace Portarr.Core.Catalog;

public sealed record CatalogState(Catalog Catalog, string FilePath, bool FileExists, IReadOnlyList<string> Errors, DateTimeOffset? LoadedAt);

public interface ICatalogService
{
    CatalogState Current { get; }

    /// <summary>Re-reads the file now (also triggered by the file watcher).</summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps catalog.yaml in memory and reloads it when the file changes. A broken edit keeps the
/// last valid catalog and reports the errors, so a typo never empties the portal.
/// </summary>
public sealed partial class CatalogService : ICatalogService, IHostedService, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly string _filePath;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogService> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _pendingReload;

    public CatalogService(IOptions<PortarrOptions> options, TimeProvider timeProvider, ILogger<CatalogService> logger)
    {
        _filePath = Path.GetFullPath(options.Value.EffectiveCatalogFile);
        _timeProvider = timeProvider;
        _logger = logger;
        Current = new CatalogState(Catalog.Empty, _filePath, false, [], null);
    }

    public CatalogState Current { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReloadAsync(cancellationToken);

        var folder = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(folder);
        _watcher = new FileSystemWatcher(folder, Path.GetFileName(_filePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => ScheduleReload();
        _watcher.Created += (_, _) => ScheduleReload();
        _watcher.Deleted += (_, _) => ScheduleReload();
        _watcher.Renamed += (_, _) => ScheduleReload();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _watcher = null;
        return Task.CompletedTask;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                Current = new CatalogState(Catalog.Empty, _filePath, false, [], _timeProvider.GetUtcNow());
                LogMissing(_filePath);
                return;
            }

            var result = CatalogParser.Parse(await File.ReadAllTextAsync(_filePath, cancellationToken));

            // On errors, keep serving the previous valid catalog.
            Current = new CatalogState(
                result.IsValid ? result.Catalog : Current.Catalog,
                _filePath,
                true,
                result.Errors,
                _timeProvider.GetUtcNow());

            if (result.IsValid)
            {
                LogLoaded(result.Catalog.Apps.Count, result.Catalog.Stacks.Count, result.Catalog.Environments.Count);
            }
            else
            {
                LogInvalid(result.Errors.Count, string.Join(" | ", result.Errors));
            }
        }
        catch (IOException exception)
        {
            // The editor may still be writing the file: the watcher fires again when it is done.
            LogUnreadable(exception.Message);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _pendingReload?.Dispose();
        _reloadLock.Dispose();
    }

    private void ScheduleReload()
    {
        // Editors write files in several steps: wait for the burst of events to settle.
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _pendingReload, next);
        previous?.Cancel();
        previous?.Dispose();

        _ = Task.Delay(Debounce, _timeProvider, next.Token)
            .ContinueWith(_ => ReloadAsync(), next.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default)
            .Unwrap();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog loaded: {Apps} apps, {Stacks} stacks, {Environments} environments")]
    private partial void LogLoaded(int apps, int stacks, int environments);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog has {Count} errors, keeping the previous version: {Errors}")]
    private partial void LogInvalid(int count, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "No catalog at {Path}: Portarr starts empty")]
    private partial void LogMissing(string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Catalog not readable yet: {Reason}")]
    private partial void LogUnreadable(string reason);
}
