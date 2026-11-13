using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Core.Infrastructure.Gpu;

/// <summary>
/// What one graphics card reports (D-120): its load, its memory, its video encoder and decoder (the part a media server such as
/// Jellyfin uses), its temperature and power. A figure the source did not report is null. <c>Id</c> is the card's own identifier
/// (its UUID), <c>Name</c> the model, <c>Driver</c> the driver version.
/// </summary>
public sealed record GpuReading(
    string Id,
    string Name,
    string? Driver,
    double? UtilizationPercent,
    long? MemoryUsedBytes,
    long? MemoryTotalBytes,
    double? EncoderPercent,
    double? DecoderPercent,
    int? EncoderSessions,
    double? TemperatureCelsius,
    double? PowerWatts,
    double? PowerLimitWatts);

/// <summary>A point of a card's chart: its load, its memory and its encoder at one moment.</summary>
public sealed record GpuPoint(DateTimeOffset At, double? UtilizationPercent, long? MemoryUsedBytes, double? EncoderPercent);

/// <summary>
/// The cards of an environment's machine (D-120): the last reading of each, the points of the live window per card (oldest
/// first), the workloads that have a card attached, and when the last read failed, why (a reason code; the last values are kept).
/// </summary>
public sealed record GpuView(
    string EnvironmentId,
    IReadOnlyList<GpuReading> Gpus,
    IReadOnlyDictionary<string, IReadOnlyList<GpuPoint>> History,
    IReadOnlyList<string> WorkloadIds,
    DateTimeOffset? SampledAt,
    string? Error);

/// <summary>
/// Capability of a connector that reads the graphics cards of a machine (D-120): an exporter of the card's driver. Read-only (GET).
/// A connector joins the contract suite (CLAUDE.md rule 8).
/// </summary>
public interface IGpuSource
{
    /// <summary>Whether the environment declares a graphics card to read.</summary>
    bool Supports(CatalogEnvironment environment);

    /// <summary>One reading per card; throws when the exporter cannot be read.</summary>
    Task<IReadOnlyList<GpuReading>> ReadAsync(CatalogEnvironment environment, CancellationToken cancellationToken);
}

/// <summary>
/// Capability of a connector that knows which workloads have a graphics card attached (D-120): a Docker container started with the
/// NVIDIA runtime or a GPU device request. What it says is a fact about how the container was created, not about its load: Portarr
/// does not measure a card's load per container.
/// </summary>
public interface IGpuAttachmentSource
{
    bool Supports(CatalogEnvironment environment);

    /// <summary>The ids of the <paramref name="workloads"/> that have a card attached; throws when the runtime cannot be read.</summary>
    Task<IReadOnlySet<string>> AttachedAsync(CatalogEnvironment environment, IReadOnlyList<Workload> workloads, CancellationToken cancellationToken);
}

public interface IGpuMonitor
{
    /// <summary>The cards per environment id. In memory only, a live window (D-120).</summary>
    IReadOnlyDictionary<string, GpuView> Current { get; }
}
