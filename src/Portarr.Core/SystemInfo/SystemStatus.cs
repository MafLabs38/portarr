namespace Portarr.Core.SystemInfo;

public sealed record SystemStatus(
    string AppName,
    string Version,
    DateTimeOffset StartTime,
    TimeSpan Uptime,
    string RuntimeVersion,
    string OsDescription);
