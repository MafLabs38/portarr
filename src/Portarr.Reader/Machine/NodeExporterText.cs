using System.Globalization;
using System.Text;

namespace Portarr.Reader.Machine;

/// <summary>
/// Writes a <see cref="HostSnapshot"/> in the node_exporter text exposition format, limited to the families Portarr reads
/// (D-72, D-100): <c>node_cpu_seconds_total</c> per cpu and mode, <c>node_memory_*_bytes</c>, <c>node_load1</c> and
/// <c>node_hwmon_temp_celsius</c>. Portarr's node_exporter connector reads it unchanged, so the readings are the ones
/// node_exporter would give.
/// </summary>
public static class NodeExporterText
{
    /// <summary>Clock ticks per second of <c>/proc/stat</c> (USER_HZ), which node_exporter divides by too.</summary>
    private const double TicksPerSecond = 100;

    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    public static string Write(HostSnapshot snapshot)
    {
        var text = new StringBuilder();
        text.AppendLine("# HELP node_cpu_seconds_total Seconds the CPUs spent in each mode.");
        text.AppendLine("# TYPE node_cpu_seconds_total counter");
        foreach (var cpu in snapshot.Cpus)
        {
            Mode(text, cpu.Cpu, "idle", cpu.Idle);
            Mode(text, cpu.Cpu, "iowait", cpu.Iowait);
            Mode(text, cpu.Cpu, "irq", cpu.Irq);
            Mode(text, cpu.Cpu, "nice", cpu.Nice);
            Mode(text, cpu.Cpu, "softirq", cpu.Softirq);
            Mode(text, cpu.Cpu, "steal", cpu.Steal);
            Mode(text, cpu.Cpu, "system", cpu.System);
            Mode(text, cpu.Cpu, "user", cpu.User);
        }

        foreach (var field in HostReader.MemoryFields)
        {
            if (snapshot.MemoryBytes.TryGetValue(field, out var bytes))
            {
                text.Append("# TYPE node_memory_").Append(field).AppendLine("_bytes gauge");
                text.Append("node_memory_").Append(field).Append("_bytes ").AppendLine(bytes.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (snapshot.Load1 is { } load)
        {
            text.AppendLine("# TYPE node_load1 gauge");
            text.Append("node_load1 ").AppendLine(load.ToString("R", CultureInfo.InvariantCulture));
        }

        if (snapshot.Temperatures.Count > 0)
        {
            text.AppendLine("# TYPE node_hwmon_temp_celsius gauge");
            foreach (var sensor in snapshot.Temperatures)
            {
                text.Append("node_hwmon_temp_celsius{chip=\"").Append(Escape(sensor.Chip)).Append("\",sensor=\"").Append(Escape(sensor.Sensor)).Append("\"} ")
                    .AppendLine(sensor.Celsius.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        return text.ToString();
    }

    private static void Mode(StringBuilder text, string cpu, string mode, long ticks)
    {
        text.Append("node_cpu_seconds_total{cpu=\"").Append(Escape(cpu)).Append("\",mode=\"").Append(mode).Append("\"} ")
            .AppendLine((ticks / TicksPerSecond).ToString("R", CultureInfo.InvariantCulture));
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
