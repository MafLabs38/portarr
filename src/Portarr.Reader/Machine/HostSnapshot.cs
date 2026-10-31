namespace Portarr.Reader.Machine;

/// <summary>The kernel's cumulative CPU times of one logical cpu, in clock ticks (<c>/proc/stat</c>, USER_HZ = 100).</summary>
public readonly record struct CpuTicks(string Cpu, long User, long Nice, long System, long Idle, long Iowait, long Irq, long Softirq, long Steal);

/// <summary>A temperature sensor of a hwmon chip, in degrees Celsius.</summary>
public readonly record struct TemperatureSensor(string Chip, string Sensor, double Celsius);

/// <summary>
/// One reading of the machine as its kernel counts it: raw cumulative counters, never rates (D-30). Portarr turns two
/// readings into a load, as it does for node_exporter (D-72).
/// </summary>
public sealed record HostSnapshot(
    IReadOnlyList<CpuTicks> Cpus,
    IReadOnlyDictionary<string, long> MemoryBytes,
    double? Load1,
    IReadOnlyList<TemperatureSensor> Temperatures);
