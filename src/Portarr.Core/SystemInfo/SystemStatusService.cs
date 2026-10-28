using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using Portarr.Common;
using Portarr.Core.Configuration;
using Portarr.Core.Datastore;

namespace Portarr.Core.SystemInfo;

public interface ISystemStatusService
{
    Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reports the identity, uptime and storage of this Portarr instance. Registered as a singleton,
/// so the start time is the moment the host built its services.
/// </summary>
public sealed class SystemStatusService : ISystemStatusService
{
    private readonly TimeProvider _timeProvider;
    private readonly IDatabase _database;
    private readonly IOptions<PortarrOptions> _options;
    private readonly DateTimeOffset _startTime;

    public SystemStatusService(TimeProvider timeProvider, IDatabase database, IOptions<PortarrOptions> options)
    {
        _timeProvider = timeProvider;
        _database = database;
        _options = options;
        _startTime = timeProvider.GetUtcNow();
    }

    public async Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return new SystemStatus(
            BuildInfo.AppName,
            BuildInfo.Version,
            _startTime,
            _timeProvider.GetUtcNow() - _startTime,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            await _database.GetEngineVersionAsync(cancellationToken),
            _options.Value.DataFolder);
    }
}
