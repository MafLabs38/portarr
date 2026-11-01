namespace Portarr.Core.Status;

/// <summary>The single status vocabulary of every entity (spec §5), ordered from best to worst.</summary>
public enum HealthStatus
{
    Healthy,
    Maintenance,
    Paused,
    Unknown,
    Degraded,
    Down,
}
