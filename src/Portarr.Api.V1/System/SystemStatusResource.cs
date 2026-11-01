using Portarr.Core.SystemInfo;

namespace Portarr.Api.V1.System;

public sealed record SystemStatusResource(
    string AppName,
    string Version,
    DateTimeOffset StartTime,
    long UptimeSeconds,
    string RuntimeVersion,
    string OsDescription,
    string DatabaseEngine,
    string DataFolder);

public static class SystemStatusResourceMapper
{
    public static SystemStatusResource ToResource(this SystemStatus status)
    {
        return new SystemStatusResource(
            status.AppName,
            status.Version,
            status.StartTime,
            (long)status.Uptime.TotalSeconds,
            status.RuntimeVersion,
            status.OsDescription,
            status.DatabaseEngine,
            status.DataFolder);
    }
}
