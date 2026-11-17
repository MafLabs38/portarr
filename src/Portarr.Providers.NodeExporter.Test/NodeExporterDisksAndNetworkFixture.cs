using System.Globalization;
using System.Text;
using Portarr.Core.Infrastructure;

namespace Portarr.Providers.NodeExporter.Test;

/// <summary>What a machine's card says about its storage and its traffic (D-127): the filesystems that hold data, the physical interfaces' rate.</summary>
public sealed class NodeExporterDisksAndNetworkFixture
{
    private static readonly HostMachineKey Key = new("server-1", "server-1");

    private const string Memory = """
        node_memory_MemTotal_bytes 100
        node_memory_MemAvailable_bytes 50
        node_cpu_seconds_total{cpu="0",mode="idle"} 1
        """;

    private static async Task<HostReading> ReadAsync(NodeExporterSampler sampler, string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Memory + "\n" + text));
        return await sampler.ReadAsync(Key, stream, TestContext.Current.CancellationToken);
    }

    private static string Fs(string device, string mount, string type, double size, double avail) =>
        $"node_filesystem_size_bytes{{device=\"{device}\",fstype=\"{type}\",mountpoint=\"{mount}\"}} {size.ToString("R", CultureInfo.InvariantCulture)}\n" +
        $"node_filesystem_avail_bytes{{device=\"{device}\",fstype=\"{type}\",mountpoint=\"{mount}\"}} {avail.ToString("R", CultureInfo.InvariantCulture)}\n";

    private static string Net(double seconds, string device, double received, double transmitted) =>
        $"node_time_seconds {seconds.ToString("R", CultureInfo.InvariantCulture)}\n" +
        $"node_network_receive_bytes_total{{device=\"{device}\"}} {received.ToString("R", CultureInfo.InvariantCulture)}\n" +
        $"node_network_transmit_bytes_total{{device=\"{device}\"}} {transmitted.ToString("R", CultureInfo.InvariantCulture)}\n";

    [Fact]
    public async Task should_list_the_filesystems_that_hold_data_and_nothing_else()
    {
        var text = Fs("/dev/sda1", "/", "ext4", 30e9, 3e9)
            + Fs("/dev/sdb1", "/mnt/data", "btrfs", 1000e9, 400e9)
            + Fs("/dev/sdb1", "/mnt/data/subvol", "btrfs", 1000e9, 400e9)
            + Fs("tmpfs", "/run", "tmpfs", 1e9, 1e9)
            + Fs("overlay", "/var/lib/docker/overlay2/x/merged", "overlay", 30e9, 3e9)
            + Fs("/dev/sda1", "/var/lib/docker", "ext4", 30e9, 3e9)
            + Fs("/dev/loop1", "/snap/core/1", "squashfs", 1e8, 0)
            + Fs("/dev/sda15", "/boot/efi", "vfat", 1e8, 9e7);

        var disks = (await ReadAsync(new NodeExporterSampler(), text)).Disks!;

        disks.Select(d => (d.Mount, d.SizeBytes, d.UsedBytes)).Should().Equal(("/mnt/data", 1_000_000_000_000, 600_000_000_000), ("/", 30_000_000_000, 27_000_000_000));
    }

    [Fact]
    public async Task should_count_a_zfs_pool_once_from_what_its_datasets_reference()
    {
        // A 1 TB pool with 90 GB free: the root dataset references 10 GB, the data dataset 880 GB, and a container's volume
        // 2 GB of its 8 GB quota. Each dataset's size is what it references plus the free space (or its quota).
        var text = Fs("rpool/ROOT/pve-1", "/", "zfs", 100e9, 90e9)
            + Fs("rpool", "/rpool", "zfs", 90.1e9, 90e9)
            + Fs("rpool/data", "/rpool/data", "zfs", 970e9, 90e9)
            + Fs("rpool/data/subvol-101-disk-0", "/rpool/data/subvol-101-disk-0", "zfs", 8e9, 6e9)
            + Fs("/dev/sdb1", "/mnt/backup", "ext4", 2000e9, 500e9);

        var disks = (await ReadAsync(new NodeExporterSampler(), text)).Disks!;

        disks.Select(d => (d.Device, d.Mount, d.SizeBytes, d.UsedBytes)).Should().Equal(
            ("/dev/sdb1", "/mnt/backup", 2_000_000_000_000, 1_500_000_000_000),
            ("rpool", "/rpool", 982_100_000_000, 892_100_000_000));
    }

    [Fact]
    public async Task should_keep_only_the_largest_disks()
    {
        var text = string.Concat(Enumerable.Range(1, 12).Select(i => Fs($"/dev/sd{i}", $"/mnt/disk{i}", "xfs", i * 1e9, 0)));

        var disks = (await ReadAsync(new NodeExporterSampler(), text)).Disks!;

        disks.Should().HaveCount(NodeExporterParser.MaxDisks);
        disks[0].Mount.Should().Be("/mnt/disk12");
    }

    [Fact]
    public async Task should_report_no_disks_when_the_exporter_exposes_none()
    {
        (await ReadAsync(new NodeExporterSampler(), string.Empty)).Disks.Should().BeNull();
    }

    [Fact]
    public async Task should_measure_the_traffic_of_physical_interfaces_by_the_exporters_clock()
    {
        var sampler = new NodeExporterSampler();

        var first = await ReadAsync(sampler, Net(1000, "ens3", 1_000_000, 500_000));
        var second = await ReadAsync(sampler, Net(1002, "ens3", 3_000_000, 700_000));

        first.Network.Should().BeNull("a rate needs two reads");
        second.Network.Should().Be(new HostNetwork(1_000_000, 100_000));
    }

    private static string Quiet(string scrape) => scrape.Replace("node_time_seconds", "# ");

    [Fact]
    public async Task should_count_the_card_whatever_its_name_and_not_bridges_taps_and_loopback_twice()
    {
        var sampler = new NodeExporterSampler();
        string Scrape(double seconds, double bytes) => Net(seconds, "nic0", bytes, bytes) + Quiet(Net(seconds, "vmbr0", bytes, bytes)) + Quiet(Net(seconds, "tap101i0", bytes, bytes)) + Quiet(Net(seconds, "lo", bytes * 9, bytes * 9));

        await ReadAsync(sampler, Scrape(10, 0));
        var reading = await ReadAsync(sampler, Scrape(11, 1000));

        reading.Network.Should().Be(new HostNetwork(1000, 1000));
    }

    [Fact]
    public void should_have_no_rate_when_a_counter_went_backwards_or_time_did_not_advance()
    {
        var before = new NetworkTotals(5_000, 5_000, 100);

        NodeExporterSampler.RateBetween(before, new NetworkTotals(10, 10, 101)).Should().BeNull("an interface was reset");
        NodeExporterSampler.RateBetween(before, new NetworkTotals(6_000, 6_000, 100)).Should().BeNull("the same instant");
        NodeExporterSampler.RateBetween(before, new NetworkTotals(6_000, 6_000, null)).Should().BeNull("the exporter gave no clock");
    }
}
