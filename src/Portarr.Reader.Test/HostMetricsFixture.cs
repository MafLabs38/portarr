using System.Text;
using Portarr.Core.Infrastructure;
using Portarr.Providers.NodeExporter;
using Portarr.Reader.Machine;

namespace Portarr.Reader.Test;

/// <summary>
/// What the reader says about the machine (D-100) is read by Portarr's own node_exporter sampler: the readings are the
/// ones node_exporter would give, which is what the live infrastructure page relies on (D-72).
/// </summary>
public sealed class HostMetricsFixture : IDisposable
{
    private static readonly HostMachineKey Key = new("server-1", "server-1");

    private readonly FakeHost _host = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task should_read_the_kernel_files_and_nothing_else()
    {
        var snapshot = await new HostReader(Options()).ReadAsync(Token);

        snapshot.Cpus.Select(c => c.Cpu).Should().Equal("0", "1");
        snapshot.MemoryBytes.Keys.Should().BeEquivalentTo(HostReader.MemoryFields, "the other meminfo lines (Shmem, HugePages) are not read");
        snapshot.MemoryBytes["MemTotal"].Should().Be(16000L * 1024);
        snapshot.Load1.Should().Be(0.5);
        snapshot.Temperatures.Should().ContainSingle().Which.Should().Be(new TemperatureSensor("coretemp", "temp1", 45), "only CPU chips are read, not the disk's");
    }

    [Fact]
    public async Task should_give_portarrs_sampler_the_same_readings_as_node_exporter()
    {
        var sampler = new NodeExporterSampler();
        var first = await sampler.ReadAsync(Key, await ScrapeAsync(), Token);

        // One second later cpu0 spent 50 of 100 ticks idle and cpu1 all of its 100: a quarter of the machine was busy.
        _host.Write(cpu0: (200, 350), cpu1: (150, 400));
        var second = await sampler.ReadAsync(Key, await ScrapeAsync(), Token);

        first.CpuPercent.Should().BeNull("a load is only defined between two reads");
        second.Cores.Should().Be(2);
        second.CpuPercent.Should().Be(25);
        second.CorePercents.Should().Equal(50, 0);
        second.Memory.TotalBytes.Should().Be(16000L * 1024);
        second.Memory.UsedBytes.Should().Be((16000L - 8000) * 1024, "used = MemTotal - MemAvailable");
        second.Memory.SwapUsedBytes.Should().Be(100L * 1024);
        second.Load1.Should().Be(0.5);
        second.TemperatureCelsius.Should().Be(45);
    }

    [Fact]
    public async Task should_expose_only_the_families_portarr_reads()
    {
        var text = NodeExporterText.Write(await new HostReader(Options()).ReadAsync(Token));

        var families = text.Split('\n').Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('{', ' ')[0]).Distinct();
        families.Should().BeEquivalentTo(
            "node_cpu_seconds_total",
            "node_memory_MemTotal_bytes",
            "node_memory_MemFree_bytes",
            "node_memory_MemAvailable_bytes",
            "node_memory_Buffers_bytes",
            "node_memory_Cached_bytes",
            "node_memory_SReclaimable_bytes",
            "node_memory_SwapTotal_bytes",
            "node_memory_SwapFree_bytes",
            "node_load1",
            "node_hwmon_temp_celsius");
    }

    [Fact]
    public void should_skip_what_is_not_a_cpu_line_and_read_a_bad_number_as_zero()
    {
        var cpus = HostReader.ParseCpus("cpu  1 2 3 4 5 6 7 8\ncpu0 1 2 3\ncpu1 10 x 30 40 50 60 70 80 0 0\ncpuinfo 1 2 3 4 5 6 7 8 9\nintr 1\n");

        cpus.Should().ContainSingle("a line of fewer than 9 fields, the aggregate and 'cpuinfo' are not a core").Which.Should().Be(new CpuTicks("1", 10, 0, 30, 40, 50, 60, 70, 80));
    }

    [Fact]
    public void should_read_memory_in_kilobytes_or_bytes_and_ignore_lines_it_does_not_know()
    {
        var memory = HostReader.ParseMemory("MemTotal:  2 kB\nMemFree: 5\nBuffers: nope kB\nShmem: 9 kB\ngarbage\n");

        memory.Should().BeEquivalentTo(new Dictionary<string, long> { ["MemTotal"] = 2048, ["MemFree"] = 5 });
    }

    [Fact]
    public async Task should_read_a_machine_without_load_average_or_temperature_sensors()
    {
        File.Delete(Path.Combine(_host.Proc, "loadavg"));
        Directory.Delete(Path.Combine(_host.Sys, "class", "hwmon"), recursive: true);

        var snapshot = await new HostReader(Options()).ReadAsync(Token);

        snapshot.Load1.Should().BeNull();
        snapshot.Temperatures.Should().BeEmpty("a virtual machine has no sensor");
    }

    [Fact]
    public async Task should_refuse_a_proc_that_is_not_linuxs()
    {
        File.WriteAllText(Path.Combine(_host.Proc, "stat"), "nothing useful\n");

        var read = () => new HostReader(Options()).ReadAsync(Token);

        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task should_escape_a_chip_name_so_that_portarrs_parser_still_reads_the_line()
    {
        var snapshot = await new HostReader(Options()).ReadAsync(Token);
        var hostile = snapshot with { Temperatures = [new TemperatureSensor("coretemp\"\\\n", "temp1", 61.5)] };
        var sampler = new NodeExporterSampler();

        var reading = await sampler.ReadAsync(Key, new MemoryStream(Encoding.UTF8.GetBytes(NodeExporterText.Write(hostile))), Token);

        reading.TemperatureCelsius.Should().Be(61.5);
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private ReaderOptions Options() => new() { ProcPath = _host.Proc, SysPath = _host.Sys };

    private async Task<Stream> ScrapeAsync()
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(NodeExporterText.Write(await new HostReader(Options()).ReadAsync(Token))));
    }
}
