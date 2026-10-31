using System.Globalization;

namespace Portarr.Reader.Machine;

/// <summary>
/// Reads the host's <c>/proc</c> and <c>/sys</c> (mounted read-only): per-cpu times, a few memory fields, the load and
/// the CPU temperature. Nothing else is read, so nothing else can leak.
/// </summary>
public sealed class HostReader
{
    /// <summary>
    /// The <c>/proc/meminfo</c> fields Portarr reads (D-72): the same list as <c>NodeExporterParser.MemoryFields</c>, copied because the
    /// reader does not depend on Portarr's connectors; the host metrics tests read this text with Portarr's own sampler.
    /// </summary>
    public static readonly IReadOnlyList<string> MemoryFields =
        ["MemTotal", "MemFree", "MemAvailable", "Buffers", "Cached", "SReclaimable", "SwapTotal", "SwapFree"];

    /// <summary>hwmon chips that measure the CPU package or its cores (Intel, AMD, Raspberry Pi and other ARM boards); the same list as the sampler's.</summary>
    private static readonly string[] CpuChips = ["coretemp", "k10temp", "zenpower", "cpu_thermal", "cpu-thermal"];

    private readonly string _proc;
    private readonly string _sys;

    public HostReader(ReaderOptions options)
    {
        _proc = options.ProcPath;
        _sys = options.SysPath;
    }

    public async Task<HostSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var stat = await File.ReadAllTextAsync(Path.Combine(_proc, "stat"), cancellationToken);
        var meminfo = await File.ReadAllTextAsync(Path.Combine(_proc, "meminfo"), cancellationToken);
        var cpus = ParseCpus(stat);
        var memory = ParseMemory(meminfo);
        if (cpus.Count == 0 || !memory.ContainsKey("MemTotal") || !memory.ContainsKey("MemAvailable"))
        {
            throw new InvalidDataException("/proc/stat and /proc/meminfo do not look like Linux's (is the host's /proc mounted?).");
        }

        return new HostSnapshot(cpus, memory, await ReadLoadAsync(cancellationToken), await ReadTemperaturesAsync(cancellationToken));
    }

    /// <summary>The <c>cpuN</c> lines of <c>/proc/stat</c> (the aggregate <c>cpu</c> line is Portarr's to compute, not ours).</summary>
    public static IReadOnlyList<CpuTicks> ParseCpus(string stat)
    {
        var cpus = new List<CpuTicks>();
        foreach (var line in stat.Split('\n'))
        {
            if (!line.StartsWith("cpu", StringComparison.Ordinal) || line.Length < 4 || !char.IsDigit(line[3]))
            {
                continue;
            }

            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 9)
            {
                continue;
            }

            long Field(int index) => long.TryParse(fields[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
            cpus.Add(new CpuTicks(fields[0][3..], Field(1), Field(2), Field(3), Field(4), Field(5), Field(6), Field(7), Field(8)));
        }

        return cpus;
    }

    /// <summary>The wanted fields of <c>/proc/meminfo</c>, in bytes (the file counts kB).</summary>
    public static IReadOnlyDictionary<string, long> ParseMemory(string meminfo)
    {
        var memory = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in meminfo.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon];
            if (!MemoryFields.Contains(name))
            {
                continue;
            }

            var number = line[(colon + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (number.Length > 0 && long.TryParse(number[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                memory[name] = number.Length > 1 && number[1].Equals("kB", StringComparison.OrdinalIgnoreCase) ? value * 1024 : value;
            }
        }

        return memory;
    }

    private async Task<double?> ReadLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(Path.Combine(_proc, "loadavg"), cancellationToken);
            var first = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var load) ? load : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The temperature sensors of the CPU chips; none when the machine has no such sensor (a VM, for one).</summary>
    private async Task<IReadOnlyList<TemperatureSensor>> ReadTemperaturesAsync(CancellationToken cancellationToken)
    {
        var sensors = new List<TemperatureSensor>();
        var hwmon = Path.Combine(_sys, "class", "hwmon");
        if (!Directory.Exists(hwmon))
        {
            return sensors;
        }

        foreach (var chipDirectory in Directory.EnumerateDirectories(hwmon).Order(StringComparer.Ordinal))
        {
            try
            {
                var chip = (await File.ReadAllTextAsync(Path.Combine(chipDirectory, "name"), cancellationToken)).Trim();
                if (!CpuChips.Any(c => chip.Contains(c, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (var input in Directory.EnumerateFiles(chipDirectory, "temp*_input").Order(StringComparer.Ordinal))
                {
                    var milli = (await File.ReadAllTextAsync(input, cancellationToken)).Trim();
                    if (double.TryParse(milli, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        var file = Path.GetFileName(input);
                        sensors.Add(new TemperatureSensor(chip, file[..file.IndexOf('_', StringComparison.Ordinal)], value / 1000));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A sensor that cannot be read (a container without access to /sys, a chip that went away) is skipped: the temperature is optional.
            }
        }

        return sensors;
    }
}
