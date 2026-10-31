using System.Globalization;

namespace Portarr.Providers.NodeExporter;

/// <summary>Seconds a CPU spent idle, waiting for I/O, and in every mode together (node_cpu_seconds_total).</summary>
internal readonly record struct CpuTimes(double Idle, double Iowait, double Total);

/// <summary>The counters Portarr reads from one node_exporter scrape (D-72); null when the family is absent.</summary>
internal sealed record NodeExporterCounters(
    IReadOnlyDictionary<string, CpuTimes> Cpus,
    IReadOnlyDictionary<string, double> Memory,
    double? Load1,
    double? CpuTemperatureCelsius);

/// <summary>
/// Reads the Prometheus text exposition format (version 0.0.4) line by line, keeping only the families the
/// dashboard needs: <c>node_cpu_seconds_total</c> per cpu and mode, <c>node_memory_*_bytes</c> of
/// <see cref="MemoryFields"/>, <c>node_load1</c> and the CPU sensors of <c>node_hwmon_temp_celsius</c>. Every other
/// line is skipped without being parsed; a malformed line is skipped too (one bad sample must not hide the host).
/// Stops with <see cref="InvalidDataException"/> past a size cap (a runaway answer is not node_exporter).
/// </summary>
internal static class NodeExporterParser
{
    public const string CpuFamily = "node_cpu_seconds_total";
    public const string MemoryPrefix = "node_memory_";
    public const string Load1Family = "node_load1";
    public const string TemperatureFamily = "node_hwmon_temp_celsius";

    /// <summary>The /proc/meminfo fields read, as node_exporter names them (node_memory_&lt;field&gt;_bytes).</summary>
    public static readonly IReadOnlySet<string> MemoryFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "MemTotal", "MemFree", "MemAvailable", "Buffers", "Cached", "SReclaimable", "SwapTotal", "SwapFree",
    };

    /// <summary>hwmon chips that measure the CPU package or its cores (Intel, AMD, Raspberry Pi and other ARM boards).</summary>
    private static readonly string[] CpuChips = ["coretemp", "k10temp", "zenpower", "cpu_thermal", "cpu-thermal"];

    public static async Task<NodeExporterCounters> ParseAsync(TextReader reader, long maxCharacters, CancellationToken cancellationToken)
    {
        var cpus = new Dictionary<string, (double Idle, double Iowait, double Total)>(StringComparer.Ordinal);
        var memory = new Dictionary<string, double>(StringComparer.Ordinal);
        double? load1 = null;
        double? temperature = null;
        long read = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            read += line.Length + 1;
            if (read > maxCharacters)
            {
                throw new InvalidDataException($"The metrics answer is larger than {maxCharacters} characters.");
            }

            if (line.Length == 0 || line[0] != 'n' || !TryParseSample(line, out var sample))
            {
                continue;
            }

            switch (sample.Name)
            {
                case CpuFamily when sample.Label("cpu") is { } cpu && sample.Label("mode") is { } mode:
                    var times = cpus.GetValueOrDefault(cpu);
                    cpus[cpu] = (
                        times.Idle + (mode == "idle" ? sample.Value : 0),
                        times.Iowait + (mode == "iowait" ? sample.Value : 0),
                        times.Total + sample.Value);
                    break;
                case Load1Family:
                    load1 = sample.Value;
                    break;
                case TemperatureFamily when IsCpuChip(sample.Label("chip")) && sample.Value is > 0 and < 150:
                    temperature = Math.Max(temperature ?? double.MinValue, sample.Value);
                    break;
                default:
                    if (MemoryField(sample.Name) is { } field)
                    {
                        memory[field] = sample.Value;
                    }

                    break;
            }
        }

        return new NodeExporterCounters(
            cpus.ToDictionary(c => c.Key, c => new CpuTimes(c.Value.Idle, c.Value.Iowait, c.Value.Total), StringComparer.Ordinal),
            memory,
            load1,
            temperature);
    }

    private static string? MemoryField(string name)
    {
        if (!name.StartsWith(MemoryPrefix, StringComparison.Ordinal) || !name.EndsWith("_bytes", StringComparison.Ordinal))
        {
            return null;
        }

        var field = name[MemoryPrefix.Length..^"_bytes".Length];
        return MemoryFields.Contains(field) ? field : null;
    }

    private static bool IsCpuChip(string? chip)
    {
        return chip is not null && CpuChips.Any(c => chip.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A sample line: <c>name{label="value",…} value [timestamp]</c>.</summary>
    internal readonly record struct Sample(string Name, IReadOnlyList<KeyValuePair<string, string>> Labels, double Value)
    {
        public string? Label(string name) => Labels.FirstOrDefault(l => l.Key == name).Value;
    }

    /// <summary>
    /// Parses one sample line of a wanted family; false for any other family or a malformed line. Label
    /// values may hold escaped <c>\\</c>, <c>\"</c> and <c>\n</c>; values may be NaN, ±Inf or in exponent form.
    /// </summary>
    internal static bool TryParseSample(string line, out Sample sample)
    {
        sample = default;
        var nameEnd = line.IndexOfAny(['{', ' ', '\t']);
        if (nameEnd <= 0)
        {
            return false;
        }

        var name = line[..nameEnd];
        if (!IsWanted(name))
        {
            return false;
        }

        var labels = new List<KeyValuePair<string, string>>();
        var position = nameEnd;
        if (line[position] == '{' && !TryParseLabels(line, ref position, labels))
        {
            return false;
        }

        var rest = line.AsSpan(position).Trim();
        var valueEnd = rest.IndexOfAny(' ', '\t');
        var valueText = valueEnd < 0 ? rest : rest[..valueEnd];
        if (!TryParseValue(valueText, out var value))
        {
            return false;
        }

        sample = new Sample(name, labels, value);
        return true;
    }

    private static bool IsWanted(string name)
    {
        return name is CpuFamily or Load1Family or TemperatureFamily || name.StartsWith(MemoryPrefix, StringComparison.Ordinal);
    }

    /// <summary>From the opening brace to just after the closing one.</summary>
    private static bool TryParseLabels(string line, ref int position, List<KeyValuePair<string, string>> labels)
    {
        position++;
        while (position < line.Length)
        {
            while (position < line.Length && line[position] is ' ' or ',')
            {
                position++;
            }

            if (position < line.Length && line[position] == '}')
            {
                position++;
                return true;
            }

            var equals = line.IndexOf('=', position);
            if (equals < 0 || equals + 1 >= line.Length || line[equals + 1] != '"')
            {
                return false;
            }

            var key = line[position..equals].Trim();
            position = equals + 2;
            var value = new System.Text.StringBuilder();
            while (position < line.Length && line[position] != '"')
            {
                if (line[position] == '\\' && position + 1 < line.Length)
                {
                    position++;
                    value.Append(line[position] == 'n' ? '\n' : line[position]);
                }
                else
                {
                    value.Append(line[position]);
                }

                position++;
            }

            if (position >= line.Length)
            {
                return false;
            }

            position++;
            labels.Add(new KeyValuePair<string, string>(key, value.ToString()));
        }

        return false;
    }

    private static bool TryParseValue(ReadOnlySpan<char> text, out double value)
    {
        switch (text)
        {
            case "+Inf" or "Inf":
                value = double.PositiveInfinity;
                return true;
            case "-Inf":
                value = double.NegativeInfinity;
                return true;
            case "NaN":
                value = double.NaN;
                return true;
            default:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
