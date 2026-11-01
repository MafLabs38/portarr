using AwesomeAssertions;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>
/// An infrastructure connector wired to a tool that serves the machines of an environment: the source under test, an environment
/// it reads, one it does not, the workloads to sample usage of, how to make the tool fail (null for a real engine) and the
/// methods it saw (null when they cannot be observed).
/// </summary>
public sealed record InfrastructureSourceHarness(
    IInfrastructureSource Source,
    CatalogEnvironment Environment,
    CatalogEnvironment Unsupported,
    IReadOnlyList<Workload> Workloads,
    Action<ToolFault>? Fail = null,
    Func<IReadOnlyList<HttpMethod>>? Methods = null);

/// <summary>
/// The contract of the infrastructure capability (<see cref="IInfrastructureSource"/>, D-54), run against every connector that
/// declares it (Docker, Kubernetes): the machines of an environment with their capacity, read only, and a failure that is not
/// an empty answer. A connector joins by inheriting this class (CLAUDE.md rule 8).
/// </summary>
public abstract class InfrastructureSourceContract
{
    /// <summary>Wires the connector to a tool that serves the machines of an environment.</summary>
    protected abstract InfrastructureSourceHarness Arrange();

    [Fact]
    public void should_read_the_environment_it_is_wired_to_and_no_other()
    {
        var harness = Arrange();

        harness.Source.Supports(harness.Environment).Should().BeTrue();
        harness.Source.Supports(harness.Unsupported).Should().BeFalse();
    }

    [Fact]
    public void should_be_read_at_a_rate_that_is_a_duration()
    {
        Arrange().Source.Interval.Should().BePositive();
    }

    [Fact]
    public async Task should_see_the_machines_of_the_environment_with_a_kind_and_a_capacity()
    {
        var harness = Arrange();

        var sample = await harness.Source.SampleAsync(harness.Environment, harness.Workloads, TestContext.Current.CancellationToken);

        sample.Machines.Should().NotBeEmpty();
        sample.Machines.Select(m => m.Id).Should().OnlyHaveUniqueItems();
        sample.Machines.Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Name) && !string.IsNullOrWhiteSpace(m.Kind));
        sample.Machines.Should().OnlyContain(m => (m.CpuCapacityCores ?? 1) > 0 && (m.MemoryCapacityBytes ?? 1) > 0);
    }

    [Fact]
    public async Task should_only_read_the_tool()
    {
        var harness = Arrange();
        if (harness.Methods is null)
        {
            return; // the requests of this tool cannot be observed (a real engine behind a read-only proxy)
        }

        await harness.Source.SampleAsync(harness.Environment, harness.Workloads, TestContext.Current.CancellationToken);

        harness.Methods().Should().NotBeEmpty().And.AllSatisfy(method => method.Should().Be(HttpMethod.Get));
    }

    [Fact]
    public async Task should_fail_rather_than_see_no_machine_when_the_tool_cannot_be_reached()
    {
        var harness = Arrange();
        if (harness.Fail is null)
        {
            return; // a real engine cannot be made to fail
        }

        harness.Fail(ToolFault.Unreachable);
        var sample = () => harness.Source.SampleAsync(harness.Environment, harness.Workloads, TestContext.Current.CancellationToken);

        await sample.Should().ThrowAsync<Exception>();
    }
}
