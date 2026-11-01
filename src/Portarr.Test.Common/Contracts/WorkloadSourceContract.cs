using AwesomeAssertions;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>
/// A workload connector wired to a tool that serves one known workload: the source under test, an environment it reads, one it
/// does not, the name of a workload the tool serves, how to make the tool fail (null for a real engine) and the methods it saw
/// (null when they cannot be observed).
/// </summary>
public sealed record WorkloadSourceHarness(
    IWorkloadSource Source,
    CatalogEnvironment Environment,
    CatalogEnvironment Unsupported,
    string KnownWorkload,
    Action<ToolFault>? Fail = null,
    Func<IReadOnlyList<HttpMethod>>? Methods = null);

/// <summary>
/// The contract of the workload capability (<see cref="IWorkloadSource"/>, D-31, D-32), run against every connector that declares
/// it (Docker, Kubernetes, Cloudflare): what a source reads, how it names what it finds, that it only reads, and that it fails
/// loudly when its tool is out of reach. A connector joins by inheriting this class (CLAUDE.md rule 8).
/// </summary>
public abstract class WorkloadSourceContract
{
    /// <summary>Wires the connector to a tool that serves a workload.</summary>
    protected abstract WorkloadSourceHarness Arrange();

    [Fact]
    public void should_read_the_environment_it_is_wired_to_and_no_other()
    {
        var harness = Arrange();

        harness.Source.Supports(harness.Environment).Should().BeTrue();
        harness.Source.Supports(harness.Unsupported).Should().BeFalse();
    }

    [Fact]
    public void should_have_nothing_to_complain_about_when_it_is_configured()
    {
        var harness = Arrange();

        harness.Source.GetConfigurationError(harness.Environment).Should().BeNull();
    }

    [Fact]
    public async Task should_list_the_workload_the_tool_serves_as_one_of_the_environment()
    {
        var harness = Arrange();

        var workloads = await harness.Source.ListAsync(harness.Environment, TestContext.Current.CancellationToken);

        var known = workloads.Should().Contain(w => w.Name == harness.KnownWorkload).Subject;
        known.EnvironmentId.Should().Be(harness.Environment.Id);
        known.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task should_give_every_workload_one_identity()
    {
        var harness = Arrange();

        var workloads = await harness.Source.ListAsync(harness.Environment, TestContext.Current.CancellationToken);

        workloads.Select(w => w.Id).Should().OnlyHaveUniqueItems();
        workloads.Should().OnlyContain(w => w.EnvironmentId == harness.Environment.Id);
    }

    [Fact]
    public async Task should_say_nothing_of_a_workload_that_does_not_exist()
    {
        var harness = Arrange();

        (await harness.Source.OpenLogsAsync(harness.Environment, "no-such-workload", 10, false, TestContext.Current.CancellationToken)).Should().BeNull();
        if (harness.Source.SupportsUsage)
        {
            (await harness.Source.GetUsageAsync(harness.Environment, "no-such-workload", TestContext.Current.CancellationToken)).Should().BeNull();
        }
    }

    [Fact]
    public async Task should_only_read_the_tool()
    {
        var harness = Arrange();
        if (harness.Methods is null)
        {
            return; // the requests of this tool cannot be observed (a real engine behind a read-only proxy)
        }

        await harness.Source.ListAsync(harness.Environment, TestContext.Current.CancellationToken);

        harness.Methods().Should().NotBeEmpty().And.AllSatisfy(method => method.Should().Be(HttpMethod.Get));
    }

    [Fact]
    public async Task should_fail_rather_than_list_nothing_when_the_tool_cannot_be_reached()
    {
        var harness = Arrange();
        if (harness.Fail is null)
        {
            return; // a real engine cannot be made to fail
        }

        harness.Fail(ToolFault.Unreachable);
        var list = () => harness.Source.ListAsync(harness.Environment, TestContext.Current.CancellationToken);

        await list.Should().ThrowAsync<Exception>();
    }
}
