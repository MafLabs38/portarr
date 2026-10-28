using System.Runtime.InteropServices;
using Portarr.Common;

namespace Portarr.Core.SystemInfo;

public interface ISystemStatusService
{
    SystemStatus GetStatus();
}

/// <summary>
/// Reports the identity and uptime of this Portarr instance. Registered as a singleton,
/// so the start time is the moment the host built its services.
/// </summary>
public sealed class SystemStatusService : ISystemStatusService
{
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _startTime;

    public SystemStatusService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _startTime = timeProvider.GetUtcNow();
    }

    public SystemStatus GetStatus()
    {
        return new SystemStatus(
            BuildInfo.AppName,
            BuildInfo.Version,
            _startTime,
            _timeProvider.GetUtcNow() - _startTime,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription);
    }
}
