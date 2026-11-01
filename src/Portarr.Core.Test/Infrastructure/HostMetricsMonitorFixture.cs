using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Core.Test.Infrastructure;

/// <summary>The monitor that reads every host agent each second (D-72): per machine, never twice at once, backing off on failure.</summary>
public sealed class HostMetricsMonitorFixture
{
    private const string Catalog = """
        environments:
          - { id: server-1, docker: { url: "http://10.0.0.10:2375" }, hostMetrics: { nodeExporter: "http://10.0.0.10:9100/metrics" } }
          - { id: server-2, docker: { url: "http://10.0.0.11:2375" } }
        """;

    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly HostMachineKey Machine = new("server-1", "server-1");

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly IHostMetricsSource _source = Substitute.For<IHostMetricsSource>();
    private readonly IInfrastructureMonitor _infrastructure = Substitute.For<IInfrastructureMonitor>();
    private readonly ICatalogService _catalogService = Substitute.For<ICatalogService>();

    public HostMetricsMonitorFixture()
    {
        _source.Supports(Arg.Any<HostMetricsTarget>()).Returns(true);
        _infrastructure.Current.Returns(new Dictionary<string, EnvironmentMetrics>());
        UseCatalog(Catalog);
    }

    [Fact]
    public async Task should_read_each_machine_that_declares_an_agent_and_keep_its_points()
    {
        Answer(Reading(cpu: 12, usedBytes: 6_000));
        var monitor = Monitor();

        await monitor.SampleAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(1));
        Answer(Reading(cpu: 30, usedBytes: 7_000));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        monitor.Current.Keys.Should().Equal(Machine);
        var view = monitor.Current[Machine];
        view.Error.Should().BeNull();
        view.Latest!.CpuPercent.Should().Be(30);
        view.SampledAt.Should().Be(Now.AddSeconds(1));
        view.History.Select(p => (p.At, p.CpuPercent, p.MemoryUsedBytes)).Should().Equal((Now, 12.0, 6_000L), (Now.AddSeconds(1), 30.0, 7_000L));
    }

    [Fact]
    public async Task should_show_a_configuration_error_as_its_reason_without_reading_the_agent()
    {
        _source.GetConfigurationError(Arg.Any<HostMetricsTarget>()).Returns("token-missing");
        var monitor = Monitor();

        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        monitor.Current[Machine].Error.Should().Be("token-missing");
        await _source.DidNotReceive().ReadAsync(Arg.Any<HostMetricsTarget>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_keep_the_last_reading_with_the_reason_when_the_agent_fails_and_back_off_before_asking_again()
    {
        var monitor = Monitor();
        Answer(Reading(cpu: 12, usedBytes: 6_000));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        _clock.Advance(TimeSpan.FromSeconds(1));
        _source.ReadAsync(Arg.Any<HostMetricsTarget>(), Arg.Any<CancellationToken>()).Returns<HostReading>(_ => throw new HttpRequestException("refused", null, System.Net.HttpStatusCode.Forbidden));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        var failed = monitor.Current[Machine];
        failed.Error.Should().Be("http-403");
        failed.Latest!.CpuPercent.Should().Be(12, "the last good reading stays visible");
        failed.SampledAt.Should().Be(Now, "it says when that reading was taken");
        failed.History.Should().HaveCount(1);

        // First failure: asked again after one second; second failure: after two.
        var calls = Reads();
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);
        Reads().Should().Be(calls, "still backing off");

        _clock.Advance(TimeSpan.FromMilliseconds(500));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);
        var afterSecondFailure = Reads();
        afterSecondFailure.Should().BeGreaterThan(calls);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);
        Reads().Should().Be(afterSecondFailure, "the back-off doubled");
        _clock.Advance(TimeSpan.FromSeconds(1));
        Answer(Reading(cpu: 40, usedBytes: 8_000));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);
        monitor.Current[Machine].Error.Should().BeNull("a good reading ends the back-off");
    }

    [Fact]
    public async Task should_forget_a_machine_that_no_longer_declares_an_agent()
    {
        Answer(Reading(cpu: 12, usedBytes: 6_000));
        var monitor = Monitor();
        await monitor.SampleAsync(TestContext.Current.CancellationToken);
        monitor.Current.Should().ContainKey(Machine);

        UseCatalog("environments:\n  - { id: server-1, docker: { url: \"http://10.0.0.10:2375\" } }\n");
        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        monitor.Current.Should().BeEmpty();
    }

    [Fact]
    public async Task should_read_nothing_of_a_machine_no_connector_supports()
    {
        _source.Supports(Arg.Any<HostMetricsTarget>()).Returns(false);
        var monitor = Monitor();

        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        monitor.Current.Should().BeEmpty();
        await _source.DidNotReceive().ReadAsync(Arg.Any<HostMetricsTarget>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_stop_quietly_when_the_service_is_stopped()
    {
        Answer(Reading(cpu: 12, usedBytes: 6_000));
        var monitor = Monitor();
        await monitor.StartAsync(TestContext.Current.CancellationToken);

        // Stopped before its first pass, the loop never starts and its task is simply cancelled: wait until it has read once.
        var started = Task.Run(async () =>
        {
            while (!monitor.Current.ContainsKey(Machine))
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);
        await started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await monitor.StopAsync(TestContext.Current.CancellationToken);

        monitor.ExecuteTask!.Status.Should().Be(TaskStatus.RanToCompletion, "the host takes a cancelled loop for a crash");
    }

    [Fact]
    public async Task should_stream_each_new_reading_without_its_history()
    {
        Answer(Reading(cpu: 12, usedBytes: 6_000));
        var monitor = Monitor();
        await using var stream = monitor.WatchAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        // The first MoveNextAsync runs the iterator up to its first wait: the subscription exists when the sample is taken.
        var next = stream.MoveNextAsync();
        await monitor.SampleAsync(TestContext.Current.CancellationToken);
        var arrived = await next.AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        arrived.Should().BeTrue();
        var streamed = stream.Current;
        streamed.Key.Should().Be(Machine);
        streamed.Latest!.CpuPercent.Should().Be(12);
        streamed.History.Should().BeEmpty("the stream carries the reading, the snapshot carries the history");
    }

    [Fact]
    public void should_resolve_the_nodes_of_a_cluster_by_their_access_route()
    {
        var catalog = CatalogParser.Parse("""
            environments:
              - id: cluster
                kubernetes:
                  server: "https://10.0.0.20:6443"
                  nodeExporter:
                    port: 9100
                    access: direct
                    nodes: { behind-tunnel: api-server, elsewhere: "http://10.0.0.30:9100/metrics", disabled: off }
            """);
        catalog.Errors.Should().BeEmpty();
        var infrastructure = new Dictionary<string, EnvironmentMetrics>
        {
            ["cluster"] = new("cluster", [Node("home", "10.0.0.21"), Node("behind-tunnel", "10.0.0.22"), Node("elsewhere", null), Node("no-address", null), Node("disabled", "10.0.0.23"), Node("looped", "127.0.0.1")], new Dictionary<string, WorkloadUsageSample>(), Now, null),
        };

        var targets = HostMetricsMonitor.ResolveTargets(catalog.Catalog, infrastructure);

        targets.Select(t => (t.MachineId, t.Url?.ToString(), t.ApiServerPort, t.ConfigurationError)).Should().Equal(
            ("home", "http://10.0.0.21:9100/metrics", null, null),
            ("behind-tunnel", null, 9100, null),
            ("elsewhere", "http://10.0.0.30:9100/metrics", null, null),
            ("looped", null, null, ReasonCodes.InvalidAddress));
    }

    [Fact]
    public async Task should_show_a_node_with_an_address_it_will_not_read_as_a_reason_not_as_a_missing_card()
    {
        UseCatalog("""
            environments:
              - { id: cluster, kubernetes: { server: "https://10.0.0.20:6443", nodeExporter: { access: direct } } }
            """);
        _infrastructure.Current.Returns(new Dictionary<string, EnvironmentMetrics>
        {
            ["cluster"] = new("cluster", [Node("looped", "127.0.0.1")], new Dictionary<string, WorkloadUsageSample>(), Now, null),
        });
        var monitor = Monitor();

        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        monitor.Current[new HostMachineKey("cluster", "looped")].Error.Should().Be(ReasonCodes.InvalidAddress);
        await _source.DidNotReceive().ReadAsync(Arg.Any<HostMetricsTarget>(), Arg.Any<CancellationToken>());
    }

    private int Reads() => _source.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IHostMetricsSource.ReadAsync));

    private static MachineView Node(string name, string? address) =>
        new(new MachineSample(name, name, MachineKind.KubernetesNode, 4, 8_000, null, null, UsageScope.Host, Address: address), []);

    private static HostReading Reading(double cpu, long usedBytes) =>
        new(2, cpu, 1, [cpu, cpu], new HostMemory(16_000, 16_000 - usedBytes, 4_000, 2_000, 1_000, 1_000), 0.5, null);

    private void Answer(HostReading reading) =>
        _source.ReadAsync(Arg.Any<HostMetricsTarget>(), Arg.Any<CancellationToken>()).Returns(reading);

    private void UseCatalog(string yaml)
    {
        var parsed = CatalogParser.Parse(yaml);
        parsed.Errors.Should().BeEmpty();
        _catalogService.Current.Returns(new CatalogState(parsed.Catalog, "/config/catalog.yaml", true, [], Now));
    }

    private HostMetricsMonitor Monitor() => new(_catalogService, _infrastructure, [_source], _clock, NullLogger<HostMetricsMonitor>.Instance);
}
