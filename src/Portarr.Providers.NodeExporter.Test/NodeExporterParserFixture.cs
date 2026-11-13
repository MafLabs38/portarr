using Portarr.Core.Infrastructure;
using Portarr.Core.Metrics;

namespace Portarr.Providers.NodeExporter.Test;

public sealed class NodeExporterParserFixture
{
    private static bool Wanted(string name) => name is "node_cpu_seconds_total" or "node_load1" or "node_hwmon_temp_celsius" || name.StartsWith("node_memory_", StringComparison.Ordinal);

    private const string Scrape = """
        # HELP node_cpu_seconds_total Seconds the CPUs spent in each mode.
        # TYPE node_cpu_seconds_total counter
        node_cpu_seconds_total{cpu="0",mode="idle"} 900
        node_cpu_seconds_total{cpu="0",mode="iowait"} 20
        node_cpu_seconds_total{cpu="0",mode="user"} 60
        node_cpu_seconds_total{cpu="0",mode="system"} 20
        node_cpu_seconds_total{cpu="1",mode="idle"} 8.5e+02
        node_cpu_seconds_total{cpu="1",mode="iowait"} 0
        node_cpu_seconds_total{cpu="1",mode="user"} 150
        node_cpu_guest_seconds_total{cpu="0",mode="user"} 40
        node_memory_MemTotal_bytes 1.6777216e+10
        node_memory_MemAvailable_bytes 8.388608e+09
        node_memory_MemFree_bytes 2147483648
        node_memory_SwapTotal_bytes 0
        node_memory_Active_bytes 123
        node_load1 1.25
        node_load15 9
        node_hwmon_temp_celsius{chip="platform_coretemp_0",sensor="temp1"} 45
        node_hwmon_temp_celsius{chip="platform_coretemp_0",sensor="temp2"} 52.5
        node_hwmon_temp_celsius{chip="nvme_nvme0",sensor="temp1"} 70
        node_filesystem_avail_bytes{mountpoint="/"} 1
        """;

    private const string UnraidInfo = """
        # HELP node_os_info A metric with a constant '1' value labeled by build_id, id, id_like, image_id, image_version, name, pretty_name, variant, variant_id, version, version_codename, version_id from /etc/os-release.
        # TYPE node_os_info gauge
        node_os_info{build_id="",id="unraid-os",id_like="slackware",image_id="",image_version="",name="Unraid OS",pretty_name="Unraid OS 7.3 x86_64",variant="",variant_id="",version="7.3.4 2026-10-22",version_codename="stable",version_id="7.3.4"} 1
        # TYPE node_uname_info gauge
        node_uname_info{domainname="(none)",machine="x86_64",nodename="server-1",release="6.18.58-Unraid",sysname="Linux",version="#2 SMP PREEMPT_DYNAMIC Wed Oct 21 15:43:26 PDT 2026"} 1
        """;

    [Fact]
    public async Task should_read_the_system_from_the_os_and_uname_info_families()
    {
        var counters = await ParseAsync(UnraidInfo);

        counters.System.Should().Be(new MachineSystem("unraid-os", "Unraid OS 7.3 x86_64", "7.3.4", "6.18.58-Unraid"));
    }

    [Fact]
    public async Task should_fall_back_to_the_plain_name_and_version_and_ignore_empty_labels()
    {
        var counters = await ParseAsync("""node_os_info{id="",name="Debian GNU/Linux",pretty_name="",version="13 (trixie)",version_id=""} 1""");

        counters.System.Should().Be(new MachineSystem(null, "Debian GNU/Linux", "13 (trixie)", null));
    }

    [Fact]
    public async Task should_report_no_system_when_neither_info_family_is_exposed()
    {
        (await ParseAsync(Scrape)).System.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_only_the_families_the_dashboard_needs()
    {
        var counters = await ParseAsync(Scrape);

        counters.Cpus.Should().HaveCount(2);
        counters.Cpus["0"].Should().Be(new CpuTimes(900, 20, 1000));
        counters.Cpus["1"].Should().Be(new CpuTimes(850, 0, 1000));
        counters.Memory.Should().BeEquivalentTo(new Dictionary<string, double>
        {
            ["MemTotal"] = 16_777_216_000,
            ["MemAvailable"] = 8_388_608_000,
            ["MemFree"] = 2_147_483_648,
            ["SwapTotal"] = 0,
        });
        counters.Load1.Should().Be(1.25);
    }

    [Fact]
    public async Task should_take_the_hottest_cpu_sensor_and_ignore_disks()
    {
        var counters = await ParseAsync(Scrape);

        counters.CpuTemperatureCelsius.Should().Be(52.5);
    }

    [Theory]
    [InlineData("node_load1 NaN", double.NaN)]
    [InlineData("node_load1 +Inf", double.PositiveInfinity)]
    [InlineData("node_load1 3 1700000000000", 3)]
    [InlineData("node_load1{} 2", 2)]
    public void should_read_special_values_timestamps_and_empty_labels(string line, double expected)
    {
        PrometheusText.TryParseSample(line, Wanted, out var sample).Should().BeTrue();
        sample.Value.Should().Be(expected);
    }

    [Fact]
    public void should_unescape_label_values()
    {
        PrometheusText.TryParseSample("""node_hwmon_temp_celsius{chip="a\"b\\c",sensor="x, y"} 40""", Wanted, out var sample).Should().BeTrue();

        sample.Label("chip").Should().Be("a\"b\\c");
        sample.Label("sensor").Should().Be("x, y");
    }

    [Theory]
    [InlineData("node_load1")]
    [InlineData("node_load1 abc")]
    [InlineData("node_cpu_seconds_total{cpu=\"0\" 12")]
    [InlineData("node_cpu_seconds_total{cpu=0} 12")]
    [InlineData("node_filesystem_size_bytes 12")]
    public void should_skip_malformed_or_unwanted_lines(string line)
    {
        PrometheusText.TryParseSample(line, Wanted, out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_refuse_an_answer_larger_than_the_cap()
    {
        var huge = string.Join('\n', Enumerable.Repeat("node_load1 1", 1000));

        var act = () => NodeExporterParser.ParseAsync(new StringReader(huge), 5_000, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    private static Task<NodeExporterCounters> ParseAsync(string text)
    {
        return NodeExporterParser.ParseAsync(new StringReader(text), NodeExporterSampler.MaxScrapeBytes, TestContext.Current.CancellationToken);
    }
}
