using System.Collections.Concurrent;
using Portarr.Core.Infrastructure;

namespace Portarr.Providers.NodeExporter;

/// <summary>
/// Turns successive scrapes of a machine into readings (D-72). node_exporter exposes the kernel's cumulative
/// CPU counters (/proc/stat, in seconds per cpu and mode); a load is only defined between two scrapes:
/// <list type="bullet">
/// <item>per core <c>c</c>: Δtotal<sub>c</sub> = Δ(sum of every mode), load<sub>c</sub> = 100 × (1 − Δidle<sub>c</sub> / Δtotal<sub>c</sub>);</item>
/// <item>machine: load = 100 × (1 − ΣΔidle / ΣΔtotal), I/O wait = 100 × ΣΔiowait / ΣΔtotal, over the cores of both scrapes.</item>
/// </list>
/// The load includes I/O wait (and steal), like top's "100 − id" and Unraid's dashboard: a host stuck on its
/// disks is busy. Counters that go backwards (a reboot, a CPU taken offline) start over: that reading has no CPU.
/// A scrape that finds every counter where the previous one left it (two reads within one tick of /proc/stat) measures
/// nothing: it keeps the last load and the older baseline instead of blanking the card for a second.
/// Memory: used = MemTotal − MemAvailable; swap used = SwapTotal − SwapFree (<see cref="HostMemory"/>).
/// </summary>
public sealed class NodeExporterSampler
{
    /// <summary>Largest scrape read, in bytes (the stream cap, which also holds against an endless line or a decompression bomb) and in characters (the parser's): a large host answers a few hundred kB; more is not node_exporter.</summary>
    public const long MaxScrapeBytes = 8 * 1024 * 1024;

    private static readonly (double? Cpu, double? Iowait, IReadOnlyList<double>? Cores) NoRates = (null, null, null);

    private readonly ConcurrentDictionary<HostMachineKey, Baseline> _previous = new();

    /// <summary>Parses a scrape of <paramref name="key"/> (streamed, never buffered whole) and measures it against the previous one.</summary>
    public async Task<HostReading> ReadAsync(HostMachineKey key, Stream metrics, CancellationToken cancellationToken)
    {
        // The cap counts the bytes read (after any decompression): a line that never ends cannot fill the memory before it is counted.
        using var reader = new StreamReader(new CappedStream(metrics, MaxScrapeBytes));
        var counters = await NodeExporterParser.ParseAsync(reader, MaxScrapeBytes, cancellationToken);
        return Next(key, counters);
    }

    internal HostReading Next(HostMachineKey key, NodeExporterCounters counters)
    {
        var baseline = _previous.GetValueOrDefault(key);
        if (baseline is not null && SameCounters(baseline.Counters.Cpus, counters.Cpus))
        {
            return ToReading(counters, baseline.Rates);
        }

        var rates = baseline is null ? NoRates : CpuDelta(baseline.Counters.Cpus, counters.Cpus);
        _previous[key] = new Baseline(counters, rates);
        return ToReading(counters, rates);
    }

    internal static HostReading ToReading(NodeExporterCounters? previous, NodeExporterCounters current)
    {
        return ToReading(current, previous is null ? NoRates : CpuDelta(previous.Cpus, current.Cpus));
    }

    private static HostReading ToReading(NodeExporterCounters current, (double? Cpu, double? Iowait, IReadOnlyList<double>? Cores) rates)
    {
        var memory = current.Memory;
        long Bytes(string field) => memory.TryGetValue(field, out var value) && double.IsFinite(value) ? (long)value : 0;
        if (!memory.ContainsKey("MemTotal") || !memory.ContainsKey("MemAvailable"))
        {
            throw new InvalidDataException("node_memory_MemTotal_bytes and node_memory_MemAvailable_bytes are required (is the meminfo collector enabled?).");
        }

        var (cpu, iowait, cores) = rates;
        return new HostReading(
            current.Cpus.Count,
            cpu,
            iowait,
            cores,
            new HostMemory(
                Bytes("MemTotal"),
                Bytes("MemAvailable"),
                Bytes("MemFree"),
                Bytes("Buffers") + Bytes("Cached") + Bytes("SReclaimable"),
                Bytes("SwapTotal"),
                Bytes("SwapFree")),
            current.Load1 is { } load && double.IsFinite(load) ? Math.Round(load, 2) : null,
            current.CpuTemperatureCelsius is { } celsius ? Math.Round(celsius, 1) : null);
    }

    /// <summary>Whether no counter moved between two scrapes (the same cores, each with the same times).</summary>
    private static bool SameCounters(IReadOnlyDictionary<string, CpuTimes> previous, IReadOnlyDictionary<string, CpuTimes> current)
    {
        return previous.Count == current.Count && current.All(c => previous.TryGetValue(c.Key, out var before) && before == c.Value);
    }

    /// <summary>The machine's load and I/O wait, and the load per core in cpu order; nulls when no delta can be taken.</summary>
    internal static (double? Cpu, double? Iowait, IReadOnlyList<double>? Cores) CpuDelta(
        IReadOnlyDictionary<string, CpuTimes> previous,
        IReadOnlyDictionary<string, CpuTimes> current)
    {
        double idle = 0, iowait = 0, total = 0;
        var cores = new List<double>();
        foreach (var (cpu, now) in current.OrderBy(c => int.TryParse(c.Key, out var index) ? index : int.MaxValue).ThenBy(c => c.Key, StringComparer.Ordinal))
        {
            if (!previous.TryGetValue(cpu, out var before))
            {
                continue;
            }

            var dTotal = now.Total - before.Total;
            var dIdle = now.Idle - before.Idle;
            var dIowait = now.Iowait - before.Iowait;
            if (dTotal < 0 || dIdle < 0 || dIowait < 0)
            {
                // Counters went backwards: the machine rebooted (or the cpu went offline); start over.
                return (null, null, null);
            }

            idle += dIdle;
            iowait += dIowait;
            total += dTotal;
            cores.Add(dTotal > 0 ? Percent(1 - (dIdle / dTotal)) : 0);
        }

        return total > 0 ? (Percent(1 - (idle / total)), Percent(iowait / total), cores) : (null, null, null);
    }

    private static double Percent(double fraction) => Math.Round(Math.Clamp(fraction, 0, 1) * 100, 1);

    /// <summary>The counters of the last scrape that moved, with the load measured against the one before it.</summary>
    private sealed record Baseline(NodeExporterCounters Counters, (double? Cpu, double? Iowait, IReadOnlyList<double>? Cores) Rates);
}
