using System.Globalization;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker;

internal static class DockerMapper
{
    private const string ComposeProjectLabel = "com.docker.compose.project";

    public static Workload ToWorkload(string environmentId, ContainerSummary container)
    {
        var labels = container.Labels ?? new Dictionary<string, string>();

        return new Workload(
            environmentId,
            container.Id,
            container.Names.Count > 0 ? container.Names[0].TrimStart('/') : container.Id[..12],
            container.Image,
            ParseState(container.State),
            ParseHealth(container.Status),
            container.Status,
            DateTimeOffset.FromUnixTimeSeconds(container.Created),
            labels.GetValueOrDefault(ComposeProjectLabel),
            labels,
            ToPublishedPorts(container.Ports ?? []));
    }

    /// <summary>Docker lists a port once per address family (0.0.0.0 and ::): keep one entry per mapping.</summary>
    public static IReadOnlyList<PublishedPort> ToPublishedPorts(IReadOnlyList<ContainerPort> ports)
    {
        return ports
            .Where(port => port.PublicPort is > 0)
            .Select(port => new PublishedPort(port.PrivatePort, port.PublicPort!.Value, port.Type))
            .Distinct()
            .OrderBy(port => port.PrivatePort)
            .ToList();
    }

    public static WorkloadState ParseState(string state)
    {
        return Enum.TryParse<WorkloadState>(state, ignoreCase: true, out var parsed) ? parsed : WorkloadState.Unknown;
    }

    /// <summary>The list endpoint only carries health in its status text: "Up 3 hours (healthy)".</summary>
    public static WorkloadHealth ParseHealth(string status)
    {
        if (status.Contains("(unhealthy)", StringComparison.Ordinal))
        {
            return WorkloadHealth.Unhealthy;
        }

        if (status.Contains("(healthy)", StringComparison.Ordinal))
        {
            return WorkloadHealth.Healthy;
        }

        return status.Contains("(health: starting)", StringComparison.Ordinal) ? WorkloadHealth.Starting : WorkloadHealth.None;
    }

    /// <summary>
    /// CPU in cores (the share of the host's CPU time since the previous sample, times the online
    /// CPUs) and memory without the page cache, as <c>docker stats</c> shows it.
    /// </summary>
    public static WorkloadUsage ToUsage(ContainerStats stats)
    {
        var cpuDelta = (double)stats.CpuStats.CpuUsage.TotalUsage - stats.PreCpuStats.CpuUsage.TotalUsage;
        var systemDelta = (double)(stats.CpuStats.SystemCpuUsage ?? 0) - (stats.PreCpuStats.SystemCpuUsage ?? 0);
        var onlineCpus = stats.CpuStats.OnlineCpus ?? 1;
        // Without a previous sample (first read, one-shot stats) there is no interval to measure.
        var hasPrevious = stats.PreCpuStats.SystemCpuUsage is > 0;
        var cores = hasPrevious && cpuDelta > 0 && systemDelta > 0 ? cpuDelta / systemDelta * onlineCpus : 0;

        var memoryStats = stats.MemoryStats.Stats ?? new Dictionary<string, long>();
        var cache = memoryStats.GetValueOrDefault("inactive_file", memoryStats.GetValueOrDefault("total_inactive_file"));
        var memory = Math.Max(0, (stats.MemoryStats.Usage ?? 0) - cache);

        return new WorkloadUsage(Math.Round(cores, 3), memory, stats.MemoryStats.Limit);
    }

    /// <summary>Splits "2026-10-28T15:11:52.196383829Z message" into its timestamp and text.</summary>
    public static LogLine ParseLogLine(LogStream stream, string line)
    {
        var space = line.IndexOf(' ', StringComparison.Ordinal);
        if (space > 0 && TryParseTimestamp(line[..space], out var timestamp))
        {
            return new LogLine(timestamp, stream, line[(space + 1)..]);
        }

        return new LogLine(null, stream, line);
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
    {
        // Docker writes nanoseconds; .NET parses at most 7 fractional digits.
        var dot = value.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && value.EndsWith('Z') && value.Length - dot - 2 > 7)
        {
            value = string.Concat(value.AsSpan(0, dot + 8), "Z");
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
    }
}
