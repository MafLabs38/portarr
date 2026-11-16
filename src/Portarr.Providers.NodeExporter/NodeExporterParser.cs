using System.Globalization;
using Portarr.Core.Infrastructure;
using Portarr.Core.Metrics;

namespace Portarr.Providers.NodeExporter;

/// <summary>Seconds a CPU spent idle, waiting for I/O, and in every mode together (node_cpu_seconds_total).</summary>
internal readonly record struct CpuTimes(double Idle, double Iowait, double Total);

/// <summary>The counters Portarr reads from one node_exporter scrape (D-72); null when the family is absent.</summary>
internal sealed record NodeExporterCounters(
    IReadOnlyDictionary<string, CpuTimes> Cpus,
    IReadOnlyDictionary<string, double> Memory,
    double? Load1,
    double? CpuTemperatureCelsius,
    MachineSystem? System = null,
    IReadOnlyList<HostDisk>? Disks = null,
    NetworkTotals? Network = null);

/// <summary>The cumulative bytes the physical interfaces received and transmitted, and the exporter's own clock when it was read (seconds), to turn two scrapes into a rate.</summary>
internal sealed record NetworkTotals(double ReceiveBytes, double TransmitBytes, double? Seconds);

/// <summary>
/// Reads the Prometheus text exposition format (version 0.0.4) line by line, keeping only the families the
/// dashboard needs: <c>node_cpu_seconds_total</c> per cpu and mode, <c>node_memory_*_bytes</c> of
/// <see cref="MemoryFields"/>, <c>node_load1</c>, the CPU sensors of <c>node_hwmon_temp_celsius</c> and the labels of <c>node_os_info</c> and <c>node_uname_info</c> (the system, D-118). Every other
/// line is skipped without being parsed; a malformed line is skipped too (one bad sample must not hide the host).
/// Stops with <see cref="InvalidDataException"/> past a size cap (a runaway answer is not node_exporter).
/// </summary>
internal static class NodeExporterParser
{
    public const string CpuFamily = "node_cpu_seconds_total";
    public const string MemoryPrefix = "node_memory_";
    public const string Load1Family = "node_load1";
    public const string TemperatureFamily = "node_hwmon_temp_celsius";
    public const string OsInfoFamily = "node_os_info";
    public const string UnameFamily = "node_uname_info";
    public const string FilesystemSizeFamily = "node_filesystem_size_bytes";
    public const string FilesystemAvailFamily = "node_filesystem_avail_bytes";
    public const string NetworkReceiveFamily = "node_network_receive_bytes_total";
    public const string NetworkTransmitFamily = "node_network_transmit_bytes_total";
    public const string TimeFamily = "node_time_seconds";

    /// <summary>How many disks a machine's card lists at most: the largest ones (an Unraid array has many, a VM has one).</summary>
    public const int MaxDisks = 8;

    /// <summary>The filesystems that hold data a person cares about; tmpfs, overlay, squashfs (snaps), proc and the like are not disks.</summary>
    private static readonly IReadOnlySet<string> DiskTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ext2", "ext3", "ext4", "xfs", "btrfs", "zfs", "f2fs", "vfat", "exfat", "ntfs", "ntfs3", "fuseblk",
    };

    /// <summary>Interfaces that carry traffic a real card has already counted (bridges, VM and container interfaces, tunnels): left out of the traffic. Proxmox names its card <c>nic0</c>, so the physical ones are what remains.</summary>
    private static readonly string[] VirtualInterfaces =
    [
        "lo", "veth", "docker", "br", "vmbr", "virbr", "tap", "fwbr", "fwln", "fwpr", "vnet", "vhost", "shim", "cni", "flannel", "cali", "cilium", "lxc", "tun", "wg", "kube", "dummy", "ifb", "vxlan", "bond", "tailscale", "zt", "podman", "nebula",
    ];

    /// <summary>Mounts that belong to the container engines or the system itself, not to the machine's storage.</summary>
    private static readonly string[] SystemMounts = ["/var/lib/docker", "/var/lib/kubelet", "/var/lib/containers", "/var/lib/rancher", "/run", "/sys", "/proc", "/dev", "/snap", "/boot/efi"];

    /// <summary>The /proc/meminfo fields read, as node_exporter names them (node_memory_&lt;field&gt;_bytes).</summary>
    public static readonly IReadOnlySet<string> MemoryFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "MemTotal", "MemFree", "MemAvailable", "Buffers", "Cached", "SReclaimable", "SwapTotal", "SwapFree",
    };

    /// <summary>hwmon chips that measure the CPU package or its cores (Intel, AMD, Raspberry Pi and other ARM boards).</summary>
    private static readonly string[] CpuChips = ["coretemp", "k10temp", "zenpower", "cpu_thermal", "cpu-thermal"];

    private static bool IsWanted(string name)
    {
        return name is CpuFamily or Load1Family or TemperatureFamily or OsInfoFamily or UnameFamily or FilesystemSizeFamily or FilesystemAvailFamily or NetworkReceiveFamily or NetworkTransmitFamily or TimeFamily || name.StartsWith(MemoryPrefix, StringComparison.Ordinal);
    }

    public static async Task<NodeExporterCounters> ParseAsync(TextReader reader, long maxCharacters, CancellationToken cancellationToken)
    {
        var counters = new CounterAccumulator();
        long read = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            read += line.Length + 1;
            if (read > maxCharacters)
            {
                throw new InvalidDataException($"The metrics answer is larger than {maxCharacters} characters.");
            }

            if (line.Length > 0 && line[0] == 'n' && PrometheusText.TryParseSample(line, IsWanted, out var sample))
            {
                counters.Add(sample);
            }
        }

        return counters.ToCounters();
    }

    /// <summary>Collects the wanted samples of a scrape: per-cpu times, the memory fields, the load and the hottest CPU sensor.</summary>
    private sealed class CounterAccumulator
    {
        private readonly Dictionary<string, (double Idle, double Iowait, double Total)> _cpus = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _memory = new(StringComparer.Ordinal);
        private double? _load1;
        private double? _temperature;
        private Sample? _osInfo;
        private Sample? _uname;
        private readonly Dictionary<(string Device, string Mount), (string FsType, double Size, double Avail)> _filesystems = new();
        private double? _receive;
        private double? _transmit;
        private double? _seconds;

        public void Add(Sample sample)
        {
            switch (sample.Name)
            {
                case CpuFamily:
                    AddCpuTime(sample);
                    break;
                case Load1Family:
                    _load1 = sample.Value;
                    break;
                case TemperatureFamily:
                    AddTemperature(sample);
                    break;
                case OsInfoFamily:
                    _osInfo = sample;
                    break;
                case UnameFamily:
                    _uname = sample;
                    break;
                case FilesystemSizeFamily or FilesystemAvailFamily:
                    AddFilesystem(sample);
                    break;
                case NetworkReceiveFamily or NetworkTransmitFamily:
                    AddNetwork(sample);
                    break;
                case TimeFamily:
                    _seconds = sample.Value;
                    break;
                default:
                    if (MemoryField(sample.Name) is { } field)
                    {
                        _memory[field] = sample.Value;
                    }

                    break;
            }
        }

        public NodeExporterCounters ToCounters()
        {
            return new NodeExporterCounters(
                _cpus.ToDictionary(c => c.Key, c => new CpuTimes(c.Value.Idle, c.Value.Iowait, c.Value.Total), StringComparer.Ordinal),
                _memory,
                _load1,
                _temperature,
                ToSystem(),
                ToDisks(),
                _receive is { } received && _transmit is { } transmitted ? new NetworkTotals(received, transmitted, _seconds) : null);
        }

        /// <summary>Only real network cards count: bridges, taps, veth pairs and tunnels carry traffic that a card has already counted (a bond is counted through its slaves).</summary>
        private void AddNetwork(Sample sample)
        {
            if (sample.Label("device") is not { } device || !IsPhysicalInterface(device) || !double.IsFinite(sample.Value) || sample.Value < 0)
            {
                return;
            }

            if (sample.Name == NetworkReceiveFamily)
            {
                _receive = (_receive ?? 0) + sample.Value;
            }
            else
            {
                _transmit = (_transmit ?? 0) + sample.Value;
            }
        }

        private static bool IsPhysicalInterface(string device)
        {
            return !VirtualInterfaces.Any(prefix => device.StartsWith(prefix, StringComparison.Ordinal));
        }

        private void AddFilesystem(Sample sample)
        {
            if (sample.Label("device") is not { Length: > 0 } device || sample.Label("mountpoint") is not { Length: > 0 } mount
                || sample.Label("fstype") is not { } fsType || !double.IsFinite(sample.Value))
            {
                return;
            }

            var known = _filesystems.GetValueOrDefault((device, mount), (fsType, 0, 0));
            _filesystems[(device, mount)] = sample.Name == FilesystemSizeFamily ? (fsType, sample.Value, known.Avail) : (fsType, known.Size, sample.Value);
        }

        /// <summary>The disks: one per device (its shortest mount), data filesystems only, the largest <see cref="MaxDisks"/>; null when the exporter exposes none.</summary>
        private List<HostDisk>? ToDisks()
        {
            var disks = _filesystems
                .Where(f => f.Value.Size > 0 && DiskTypes.Contains(f.Value.FsType) && !IsSystemMount(f.Key.Mount))
                .GroupBy(f => f.Key.Device, StringComparer.Ordinal)
                .Select(group => group.OrderBy(f => f.Key.Mount.Length).ThenBy(f => f.Key.Mount, StringComparer.Ordinal).First())
                .Select(f => new HostDisk(f.Key.Mount, f.Key.Device, f.Value.FsType, (long)f.Value.Size, (long)Math.Clamp(f.Value.Avail, 0, f.Value.Size)))
                .OrderByDescending(d => d.SizeBytes)
                .ThenBy(d => d.Mount, StringComparer.Ordinal)
                .Take(MaxDisks)
                .ToList();
            return disks.Count > 0 ? disks : null;
        }

        private static bool IsSystemMount(string mount)
        {
            return SystemMounts.Any(prefix => mount == prefix || mount.StartsWith(prefix + "/", StringComparison.Ordinal));
        }

        /// <summary>The system the two info families describe; null when neither is there or they say nothing.</summary>
        private MachineSystem? ToSystem()
        {
            var system = new MachineSystem(
                Text(_osInfo, "id"),
                Text(_osInfo, "pretty_name") ?? Text(_osInfo, "name"),
                Text(_osInfo, "version_id") ?? Text(_osInfo, "version"),
                Text(_uname, "release"));
            return system.IsEmpty ? null : system;
        }

        /// <summary>A label of an info sample, without surrounding blanks; null when it is absent or empty.</summary>
        private static string? Text(Sample? sample, string label)
        {
            return ReportedText.Clean(sample?.Label(label));
        }

        private void AddCpuTime(Sample sample)
        {
            if (sample.Label("cpu") is not { } cpu || sample.Label("mode") is not { } mode)
            {
                return;
            }

            var times = _cpus.GetValueOrDefault(cpu);
            _cpus[cpu] = (
                times.Idle + (mode == "idle" ? sample.Value : 0),
                times.Iowait + (mode == "iowait" ? sample.Value : 0),
                times.Total + sample.Value);
        }

        private void AddTemperature(Sample sample)
        {
            if (IsCpuChip(sample.Label("chip")) && sample.Value is > 0 and < 150)
            {
                _temperature = Math.Max(_temperature ?? double.MinValue, sample.Value);
            }
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
    }
}
