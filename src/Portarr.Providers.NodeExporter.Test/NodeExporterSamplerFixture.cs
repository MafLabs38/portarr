using Portarr.Core.Infrastructure;

namespace Portarr.Providers.NodeExporter.Test;

public sealed class NodeExporterSamplerFixture
{
    private static readonly Dictionary<string, double> Memory = new()
    {
        ["MemTotal"] = 16_000,
        ["MemAvailable"] = 10_000,
        ["MemFree"] = 4_000,
        ["Buffers"] = 500,
        ["Cached"] = 6_000,
        ["SReclaimable"] = 500,
        ["SwapTotal"] = 2_000,
        ["SwapFree"] = 1_500,
    };

    [Fact]
    public void should_measure_load_as_100_minus_idle_including_iowait_and_per_core()
    {
        var before = Counters(("0", new CpuTimes(100, 10, 200)), ("1", new CpuTimes(100, 0, 200)));
        // Over the second: core 0 spent 0.2 s idle and 0.3 s in I/O wait of 1 s; core 1 was idle 0.9 s.
        var after = Counters(("0", new CpuTimes(100.2, 10.3, 201)), ("1", new CpuTimes(100.9, 0, 201)));

        var reading = NodeExporterSampler.ToReading(before, after);

        reading.CorePercents.Should().Equal(80, 10);
        reading.CpuPercent.Should().Be(45);
        reading.IowaitPercent.Should().Be(15);
        reading.Cores.Should().Be(2);
    }

    [Fact]
    public void should_have_no_cpu_on_the_first_read_or_after_a_reboot()
    {
        var running = Counters(("0", new CpuTimes(1_000, 0, 2_000)));
        var rebooted = Counters(("0", new CpuTimes(5, 0, 10)));

        NodeExporterSampler.ToReading(null, running).CpuPercent.Should().BeNull();
        NodeExporterSampler.ToReading(running, rebooted).CpuPercent.Should().BeNull();
    }

    [Fact]
    public void should_order_cores_numerically_and_skip_a_core_that_just_appeared()
    {
        var before = Counters(("2", new CpuTimes(0, 0, 0)), ("10", new CpuTimes(0, 0, 0)));
        var after = Counters(("10", new CpuTimes(1, 0, 1)), ("2", new CpuTimes(0, 0, 1)), ("3", new CpuTimes(0, 0, 1)));

        NodeExporterSampler.CpuDelta(before.Cpus, after.Cpus).Cores.Should().Equal(100, 0);
    }

    [Fact]
    public void should_split_memory_into_used_reclaimable_and_free_that_sum_to_total()
    {
        var memory = NodeExporterSampler.ToReading(null, Counters()).Memory;

        memory.UsedBytes.Should().Be(6_000);
        memory.ReclaimableBytes.Should().Be(6_000);
        memory.FreeBytes.Should().Be(4_000);
        (memory.UsedBytes + memory.ReclaimableBytes + memory.FreeBytes).Should().Be(memory.TotalBytes);
        memory.KernelCacheBytes.Should().Be(7_000);
        memory.SwapUsedBytes.Should().Be(500);
    }

    [Fact]
    public void should_refuse_a_scrape_without_meminfo()
    {
        var act = () => NodeExporterSampler.ToReading(null, new NodeExporterCounters(new Dictionary<string, CpuTimes>(), new Dictionary<string, double>(), null, null));

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task should_measure_each_machine_against_its_own_previous_scrape()
    {
        var sampler = new NodeExporterSampler();
        var first = new HostMachineKey("server-1", "server-1");
        var second = new HostMachineKey("cluster", "node-1");

        await ReadAsync(sampler, first, 100, 200);
        await ReadAsync(sampler, second, 0, 0);
        var reading = await ReadAsync(sampler, first, 100.5, 201);

        reading.CpuPercent.Should().Be(50);
    }

    [Fact]
    public async Task should_keep_the_last_load_when_two_scrapes_find_the_counters_unmoved()
    {
        var sampler = new NodeExporterSampler();
        var key = new HostMachineKey("server-1", "server-1");

        await ReadAsync(sampler, key, 100, 200);
        var measured = await ReadAsync(sampler, key, 100.5, 201);
        var repeated = await ReadAsync(sampler, key, 100.5, 201);
        var next = await ReadAsync(sampler, key, 101, 202);

        measured.CpuPercent.Should().Be(50);
        repeated.CpuPercent.Should().Be(50, "a scrape that measures nothing does not blank the load");
        next.CpuPercent.Should().Be(50, "the load is measured against the last scrape that moved");
    }

    private static async Task<HostReading> ReadAsync(NodeExporterSampler sampler, HostMachineKey key, double idle, double total)
    {
        var text = $$"""
            node_cpu_seconds_total{cpu="0",mode="idle"} {{idle.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
            node_cpu_seconds_total{cpu="0",mode="user"} {{(total - idle).ToString(System.Globalization.CultureInfo.InvariantCulture)}}
            node_memory_MemTotal_bytes 100
            node_memory_MemAvailable_bytes 50
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return await sampler.ReadAsync(key, stream, TestContext.Current.CancellationToken);
    }

    private static NodeExporterCounters Counters(params (string Cpu, CpuTimes Times)[] cpus)
    {
        return new NodeExporterCounters(cpus.ToDictionary(c => c.Cpu, c => c.Times), Memory, 0.5, null);
    }
}
