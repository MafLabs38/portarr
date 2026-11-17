using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;

namespace Portarr.Providers.NodeExporter.Test;

/// <summary><c>runtime: machine</c> (D-126): a host with no engine to read, measured by its node_exporter.</summary>
public sealed class MachineSourceFixture
{
    private static CatalogParseResult Parse(string yaml) => CatalogParser.Parse(yaml, [new MachineEnvironmentConnector(), new NodeExporterEnvironmentConnector()]);

    private static CatalogEnvironment Hypervisor()
    {
        var parsed = Parse("environments:\n  - { id: hv, name: Hypervisor, runtime: machine, hostMetrics: { nodeExporter: \"http://10.0.0.30:9100/metrics\" } }\n");
        parsed.Errors.Should().BeEmpty();
        return parsed.Catalog.Environments.Single();
    }

    [Fact]
    public void should_accept_an_agent_on_a_machine_without_an_engine()
    {
        var environment = Hypervisor();

        environment.IsMachineOnly().Should().BeTrue();
        environment.HostMetrics().Should().Be(new HostMetricsEndpoint(new Uri("http://10.0.0.30:9100/metrics")));
    }

    [Fact]
    public void should_not_take_a_docker_host_for_a_machine_without_an_engine()
    {
        var parsed = Parse("environments:\n  - { id: plain, name: Plain }\n");

        parsed.Catalog.Environments.Single().IsMachineOnly().Should().BeFalse();
        new MachineSource().Supports(parsed.Catalog.Environments.Single()).Should().BeFalse();
    }

    [Fact]
    public async Task should_list_no_workload_and_sample_one_machine_left_to_its_agent()
    {
        var subject = new MachineSource();
        var environment = Hypervisor();

        var workloads = await subject.ListAsync(environment, TestContext.Current.CancellationToken);
        var sample = await subject.SampleAsync(environment, workloads, TestContext.Current.CancellationToken);

        subject.Supports(environment).Should().BeTrue();
        workloads.Should().BeEmpty();
        var machine = sample.Machines.Should().ContainSingle().Subject;
        machine.Id.Should().Be("hv");
        machine.Kind.Should().Be(MachineSource.MachineKind);
        machine.Scope.Should().Be(UsageScope.Host);
        machine.CpuCapacityCores.Should().BeNull("the host agent says how many cores there are");
    }
}
