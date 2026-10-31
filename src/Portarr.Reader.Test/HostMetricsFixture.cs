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
