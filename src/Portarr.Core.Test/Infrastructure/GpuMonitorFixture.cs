using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure.Gpu;
using Portarr.Core.Workloads;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Infrastructure;

public sealed class GpuMonitorFixture
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly ICatalogService _catalog = Substitute.For<ICatalogService>();
    private readonly IWorkloadInventory _inventory = Substitute.For<IWorkloadInventory>();
    private readonly IGpuSource _source = Substitute.For<IGpuSource>();
    private readonly IGpuAttachmentSource _attachments = Substitute.For<IGpuAttachmentSource>();
    private double _load = 10;

    public GpuMonitorFixture()
    {
        var parsed = TestCatalog.Parse("environments:\n  - { id: server-1, name: Server 1 }\n  - { id: server-2, name: Server 2 }\n");
        _catalog.Current.Returns(new CatalogState(parsed.Catalog, "catalog.yaml", true, [], Start));
        _source.Supports(Arg.Is<CatalogEnvironment>(e => e.Id == "server-1")).Returns(true);
        _source.ReadAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<CancellationToken>()).Returns(_ => [Card(_load)]);
        _attachments.Supports(Arg.Any<CatalogEnvironment>()).Returns(true);
        _attachments.AttachedAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<IReadOnlyList<Workload>>(), Arg.Any<CancellationToken>()).Returns(call => (IReadOnlySet<string>)new HashSet<string> { "jellyfin" });
        var workloads = new[] { Container("jellyfin", WorkloadState.Running), Container("sonarr", WorkloadState.Running), Container("stopped", WorkloadState.Exited) };
        _inventory.Current.Returns(new Dictionary<string, EnvironmentInventory> { ["server-1"] = new("server-1", true, null, workloads, Start) });
    }

    private GpuMonitor Monitor => new(_catalog, _inventory, [_source], [_attachments], _clock, NullLogger<GpuMonitor>.Instance);

    private static GpuReading Card(double load) => new("GPU-1", "Card", "1.0", load, 1_000, 8_000, 5, 0, 1, 40, 50, 225);

    private static Workload Container(string id, WorkloadState state) => new("server-1", id, id, "example/x:1", state, WorkloadHealth.None, "", Start, null, new Dictionary<string, string>());

    [Fact]
    public async Task should_read_the_cards_of_the_environments_that_declare_one_and_only_those()
    {
        var monitor = Monitor;

        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        monitor.Current.Keys.Should().Equal("server-1");
        var view = monitor.Current["server-1"];
        view.Gpus.Should().ContainSingle().Which.UtilizationPercent.Should().Be(10);
        view.SampledAt.Should().Be(Start);
        view.Error.Should().BeNull();
    }

    [Fact]
    public async Task should_name_the_workloads_that_have_a_card_attached_among_the_running_ones_only()
    {
        var monitor = Monitor;

        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        monitor.Current["server-1"].WorkloadIds.Should().Equal("jellyfin");
        await _attachments.Received().AttachedAsync(Arg.Any<CatalogEnvironment>(), Arg.Is<IReadOnlyList<Workload>>(w => w.Select(x => x.Id).SequenceEqual(new[] { "jellyfin", "sonarr" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_keep_a_window_of_points_per_card_and_drop_what_fell_out_of_it()
    {
        var monitor = Monitor;
        await monitor.SampleAsync(TestContext.Current.CancellationToken);
        _load = 90;
        _clock.Advance(GpuMonitor.Window - TimeSpan.FromSeconds(1));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        monitor.Current["server-1"].History["GPU-1"].Select(p => p.UtilizationPercent).Should().Equal(10, 90);

        _clock.Advance(TimeSpan.FromSeconds(2));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        monitor.Current["server-1"].History["GPU-1"].Select(p => p.UtilizationPercent).Should().Equal(90, 90);
    }

    [Fact]
    public async Task should_keep_the_last_values_and_say_why_when_the_exporter_cannot_be_read()
    {
        var monitor = Monitor;
        await monitor.SampleAsync(TestContext.Current.CancellationToken);
        _source.ReadAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<GpuReading>>(_ => throw new HttpRequestException("down"));
        _clock.Advance(GpuMonitor.Interval);

        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        var view = monitor.Current["server-1"];
        view.Error.Should().Be("unreachable");
        view.Gpus.Should().ContainSingle().Which.UtilizationPercent.Should().Be(10);
        view.SampledAt.Should().Be(Start, "the time of the last reading that worked");
    }

    [Fact]
    public async Task should_say_why_there_is_nothing_when_the_very_first_read_fails()
    {
        _source.ReadAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<GpuReading>>(_ => throw new TaskCanceledException());

        var monitor = Monitor;
        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        var view = monitor.Current["server-1"];
        (view.Gpus, view.Error, view.SampledAt).Should().Be((Array.Empty<GpuReading>(), "timeout", (DateTimeOffset?)null));
    }

    [Fact]
    public async Task should_keep_the_known_attachments_when_the_runtime_cannot_be_asked()
    {
        var monitor = Monitor;
        await monitor.SampleAsync(TestContext.Current.CancellationToken);
        _attachments.AttachedAsync(Arg.Any<CatalogEnvironment>(), Arg.Any<IReadOnlyList<Workload>>(), Arg.Any<CancellationToken>()).Returns<IReadOnlySet<string>>(_ => throw new HttpRequestException("down"));

        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        monitor.Current["server-1"].WorkloadIds.Should().Equal("jellyfin");
        monitor.Current["server-1"].Error.Should().BeNull("the card itself was read");
    }
}
