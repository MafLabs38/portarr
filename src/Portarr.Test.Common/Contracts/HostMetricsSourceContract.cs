using AwesomeAssertions;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>A connector wired to a fake host agent: it serves whatever text it is given, or fails.</summary>
public sealed record HostMetricsHarness(
    IHostMetricsSource Source,
    HostMetricsTarget Target,
    HostMetricsTarget UnsupportedTarget,
    Action<string> Serve,
    Action<ToolFault> Fail,
    Func<IReadOnlyList<RecordedRequest>> Requests);

/// <summary>
/// The contract of the host metrics capability (<see cref="IHostMetricsSource"/>, D-72), run against every connector
/// that declares it (node_exporter over HTTP, node_exporter through the Kubernetes pod proxy): the same
/// exposition text in, the same readings out. A connector joins by inheriting this class (CLAUDE.md rule 8).
/// </summary>
public abstract class HostMetricsSourceContract
{
    /// <summary>Wires the connector to a fake host agent.</summary>
    protected abstract HostMetricsHarness Arrange();

    /// <summary>A scrape of one core with these cumulative seconds, and 16 000 bytes of memory of which 10 000 are available.</summary>
    protected static string Exposition(double idle, double busy) => $$"""
        # HELP node_cpu_seconds_total Seconds the CPUs spent in each mode.
        node_cpu_seconds_total{cpu="0",mode="idle"} {{idle.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
        node_cpu_seconds_total{cpu="0",mode="user"} {{busy.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
        node_memory_MemTotal_bytes 16000
        node_memory_MemAvailable_bytes 10000
        node_memory_MemFree_bytes 4000
        node_load1 0.5
        """;

    [Fact]
    public void should_support_the_targets_it_can_reach_and_no_other()
    {
        var harness = Arrange();

        harness.Source.Supports(harness.Target).Should().BeTrue();
        harness.Source.Supports(harness.UnsupportedTarget).Should().BeFalse();
    }

    [Fact]
    public async Task should_read_the_memory_the_kernel_reports_and_have_no_cpu_on_the_first_read()
    {
        var harness = Arrange();
        harness.Serve(Exposition(900, 100));

        var reading = await harness.Source.ReadAsync(harness.Target, TestContext.Current.CancellationToken);

        reading.Cores.Should().Be(1);
        reading.CpuPercent.Should().BeNull("a load is only defined between two reads");
        reading.Memory.TotalBytes.Should().Be(16_000);
        reading.Memory.UsedBytes.Should().Be(6_000, "used is the total minus what is available");
        reading.Load1.Should().Be(0.5);
    }

    [Fact]
    public async Task should_measure_the_cpu_between_two_reads()
    {
        var harness = Arrange();
        harness.Serve(Exposition(900, 100));
        await harness.Source.ReadAsync(harness.Target, TestContext.Current.CancellationToken);

        // In the second between the reads the core was idle for half of it.
        harness.Serve(Exposition(900.5, 100.5));
        var reading = await harness.Source.ReadAsync(harness.Target, TestContext.Current.CancellationToken);

        reading.CpuPercent.Should().Be(50);
    }

    [Fact]
    public async Task should_fail_when_the_agent_does_not_report_memory()
    {
        var harness = Arrange();
        harness.Serve("node_cpu_seconds_total{cpu=\"0\",mode=\"idle\"} 1\n");

        var read = () => harness.Source.ReadAsync(harness.Target, TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidDataException>("this is not a node_exporter scrape worth showing");
    }

    [Fact]
    public async Task should_fail_with_the_status_when_the_agent_refuses()
    {
        var harness = Arrange();
        harness.Fail(ToolFault.Refused);

        var read = () => harness.Source.ReadAsync(harness.Target, TestContext.Current.CancellationToken);

        (await read.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_fail_when_the_agent_cannot_be_reached()
    {
        var harness = Arrange();
        harness.Fail(ToolFault.Unreachable);

        var read = () => harness.Source.ReadAsync(harness.Target, TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<HttpRequestException>("the monitor handles a refused or timed-out read, not any exception");
    }

    [Fact]
    public async Task should_only_read_the_agent_with_get()
    {
        var harness = Arrange();
        harness.Serve(Exposition(900, 100));

        await harness.Source.ReadAsync(harness.Target, TestContext.Current.CancellationToken);

        harness.Requests().Should().NotBeEmpty().And.AllSatisfy(r => r.Method.Should().Be(HttpMethod.Get));
    }
}
