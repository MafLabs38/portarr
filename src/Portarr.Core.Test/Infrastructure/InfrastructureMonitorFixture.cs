using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Infrastructure;

public sealed class InfrastructureMonitorFixture
{
    private static readonly CatalogEnvironment Server = TestEnvironments.DockerHost("server-1", "Server 1");
    private static readonly MachineSample Machine = new("server-1", "Server 1", MachineKind.DockerHost, 8, 16_000_000_000, 1.5, 4_000_000_000, UsageScope.Containers);

    private readonly IInfrastructureSource _source = Substitute.For<IInfrastructureSource>();
    private readonly IWorkloadInventory _inventory = Substitute.For<IWorkloadInventory>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public InfrastructureMonitorFixture()
    {
        _source.Supports(Arg.Any<CatalogEnvironment>()).Returns(true);
        _inventory.Current.Returns(new Dictionary<string, EnvironmentInventory>
        {
            ["server-1"] = new("server-1", true, null, [], _clock.GetUtcNow()),
        });
        _source.SampleAsync(Server, Arg.Any<IReadOnlyList<Workload>>(), Arg.Any<CancellationToken>())
            .Returns(new InfrastructureSample([Machine], [new WorkloadUsageSample("c1", 0.5, 100)]));
    }

    [Fact]
    public async Task should_keep_a_live_window_of_ten_minutes_per_machine()
    {
        var subject = Subject();
        var interval = TimeSpan.FromSeconds(3);

        for (var i = 0; i < 250; i++)
        {
            await subject.SampleAsync(TestContext.Current.CancellationToken);
            _clock.Advance(interval);
        }

        var metrics = subject.Current["server-1"];
        var machine = metrics.Machines.Should().ContainSingle().Subject;
        machine.History[^1].CpuCores.Should().Be(1.5);
        (machine.History[^1].At - machine.History[0].At).Should().BeLessThanOrEqualTo(InfrastructureMonitor.Window);
        machine.History.Should().HaveCount(201, "10 minutes at 3 s, both ends included");
        metrics.Workloads["c1"].CpuCores.Should().Be(0.5);
        metrics.SampledAt.Should().Be(machine.History[^1].At);
    }

    [Fact]
    public async Task should_not_add_a_point_when_the_measurement_did_not_change()
    {
        // metrics.k8s.io refreshes every ~15 s: polling every 5 s returns the same timestamp twice.
        var measuredAt = _clock.GetUtcNow().AddSeconds(-4);
        _source.SampleAsync(Server, Arg.Any<IReadOnlyList<Workload>>(), Arg.Any<CancellationToken>())
            .Returns(new InfrastructureSample([Machine with { MeasuredAt = measuredAt }], [], TimeSpan.FromSeconds(20)));
        var subject = Subject();

        await subject.SampleAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(5));
        await subject.SampleAsync(TestContext.Current.CancellationToken);

        var metrics = subject.Current["server-1"];
        metrics.Machines[0].History.Should().ContainSingle().Which.At.Should().Be(measuredAt);
        metrics.SampledAt.Should().Be(measuredAt);
        metrics.Resolution.Should().Be(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task should_show_the_vms_of_a_hypervisor_as_they_report_themselves()
    {
        var hypervisor = TestEnvironments.DockerHost("hypervisor", "Hypervisor");
        var vm = TestEnvironments.DockerHost("vm-1", "VM 1", host: "hypervisor");
        var cluster = new CatalogEnvironment(
            "cluster",
            "Cluster",
            null,
            Kubernetes: new KubernetesCluster(new Uri("https://cluster:6443"), "K8S_TOKEN", null, new Dictionary<string, string> { ["node-1"] = "hypervisor" }));
        _inventory.Current.Returns(new[] { hypervisor, vm, cluster }.ToDictionary(e => e.Id, e => new EnvironmentInventory(e.Id, true, null, [], _clock.GetUtcNow())));
        _source.SampleAsync(hypervisor, Arg.Any<IReadOnlyList<Workload>>(), Arg.Any<CancellationToken>())
            .Returns(new InfrastructureSample([new MachineSample("hypervisor", "Hypervisor", MachineKind.DockerHost, 8, 64_000_000_000, 0.5, 8_000_000_000, UsageScope.Containers)], []));
        _source.SampleAsync(vm, Arg.Any<IReadOnlyList<Workload>>(), Arg.Any<CancellationToken>())
            .Returns(new InfrastructureSample([new MachineSample("vm-1", "VM 1", MachineKind.DockerHost, 4, 12_000_000_000, 0.25, 1_000_000_000, UsageScope.Containers)], []));
        _source.SampleAsync(cluster, Arg.Any<IReadOnlyList<Workload>>(), Arg.Any<CancellationToken>())
            .Returns(new InfrastructureSample(
                [
                    new MachineSample("node-1", "node-1", MachineKind.KubernetesNode, 4, 20_000_000_000, 0.75, 4_000_000_000, UsageScope.Host),
                    new MachineSample("node-2", "node-2", MachineKind.KubernetesNode, 4, 8_000_000_000, 0.1, 1_000_000_000, UsageScope.Host),
                ],
                []));
        var subject = Subject(hypervisor, vm, cluster);

        // The guests are read from the latest inside view: the second round sees all of them.
        await subject.SampleAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(3));
        await subject.SampleAsync(TestContext.Current.CancellationToken);

        var host = subject.Current["hypervisor"].Machines.Should().ContainSingle().Subject;
        host.HostEnvironmentId.Should().BeNull();
        host.Guests.Should().BeEquivalentTo(
        [
            new GuestMachine("vm-1", "vm-1", "VM 1", MachineKind.DockerHost, 4, 12_000_000_000, 0.25, UsageScope.Containers),
            new GuestMachine("cluster", "node-1", "node-1", MachineKind.KubernetesNode, 4, 20_000_000_000, 0.75, UsageScope.Host),
        ]);
        host.History[^1].GuestCpuCores.Should().Be(1.0);
        subject.Current["vm-1"].Machines[0].HostEnvironmentId.Should().Be("hypervisor");
        subject.Current["cluster"].Machines.Select(m => m.HostEnvironmentId).Should().Equal("hypervisor", null);
    }

    [Fact]
    public async Task should_keep_the_history_and_report_a_reason_code_when_sampling_fails()
    {
        var subject = Subject();
        await subject.SampleAsync(TestContext.Current.CancellationToken);

        _source.SampleAsync(Server, Arg.Any<IReadOnlyList<Workload>>(), Arg.Any<CancellationToken>())
            .Returns<InfrastructureSample>(_ => throw new HttpRequestException("Connection refused"));
        await subject.SampleAsync(TestContext.Current.CancellationToken);

        var metrics = subject.Current["server-1"];
        metrics.Error.Should().Be("unreachable");
        metrics.Machines.Should().ContainSingle().Which.History.Should().HaveCount(1);
    }

    [Fact]
    public async Task should_not_sample_an_environment_that_is_not_connected()
    {
        _inventory.Current.Returns(new Dictionary<string, EnvironmentInventory>
        {
            ["server-1"] = new("server-1", false, "token-missing", [], null, NotConfigured: true),
        });
        var subject = Subject();

        await subject.SampleAsync(TestContext.Current.CancellationToken);

        subject.Current["server-1"].Error.Should().Be("token-missing");
        await _source.DidNotReceiveWithAnyArgs().SampleAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    private InfrastructureMonitor Subject(params CatalogEnvironment[] environments)
    {
        var catalog = Substitute.For<ICatalogService>();
        catalog.Current.Returns(new CatalogState(new Portarr.Core.Catalog.Catalog(environments.Length > 0 ? environments : [Server], [], []), "catalog.yaml", true, [], null));
        return new InfrastructureMonitor(catalog, _inventory, [_source], _clock, NullLogger<InfrastructureMonitor>.Instance);
    }
}
