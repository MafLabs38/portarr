using Portarr.Core.Catalog;
using Portarr.Core.Status;
using Portarr.Core.Workloads;

namespace Portarr.Core.Test.Status;

public sealed class StatusResolverFixture
{
    private static readonly WorkloadSelector Selector = new("server-1", "sonarr");

    [Theory]
    [InlineData(WorkloadState.Running, WorkloadHealth.None, HealthStatus.Healthy)]
    [InlineData(WorkloadState.Running, WorkloadHealth.Healthy, HealthStatus.Healthy)]
    [InlineData(WorkloadState.Running, WorkloadHealth.Starting, HealthStatus.Unknown)]
    [InlineData(WorkloadState.Running, WorkloadHealth.Unhealthy, HealthStatus.Down)]
    [InlineData(WorkloadState.Restarting, WorkloadHealth.None, HealthStatus.Down)]
    [InlineData(WorkloadState.Exited, WorkloadHealth.None, HealthStatus.Down)]
    [InlineData(WorkloadState.Paused, WorkloadHealth.None, HealthStatus.Paused)]
    [InlineData(WorkloadState.Created, WorkloadHealth.None, HealthStatus.Unknown)]
    [InlineData(WorkloadState.Unknown, WorkloadHealth.None, HealthStatus.Unknown)]
    public void should_map_container_state_and_health(WorkloadState state, WorkloadHealth health, HealthStatus expected)
    {
        StatusResolver.ForWorkload(Workload(state, health)).Status.Should().Be(expected);
    }

    [Fact]
    public void should_be_unknown_when_the_app_has_no_workload()
    {
        StatusResolver.ForApp([]).Should().Be(new ResolvedStatus(HealthStatus.Unknown, "Not linked to a workload"));
    }

    [Fact]
    public void should_be_unknown_before_the_first_inventory()
    {
        StatusResolver.ForApp([(Selector, null, null)]).Reason.Should().Be("server-1 not inventoried yet");
    }

    [Fact]
    public void should_be_down_when_the_container_is_missing()
    {
        var status = StatusResolver.ForApp([(Selector, Inventory(reachable: true), null)]);

        status.Status.Should().Be(HealthStatus.Down);
        status.Reason.Should().Contain("not found");
    }

    [Fact]
    public void should_be_unknown_when_the_environment_is_unreachable()
    {
        var status = StatusResolver.ForApp([(Selector, Inventory(reachable: false), Workload(WorkloadState.Running, WorkloadHealth.None))]);

        status.Status.Should().Be(HealthStatus.Unknown);
        status.Reason.Should().Contain("unreachable");
    }

    [Fact]
    public void should_take_the_worst_workload_of_an_app()
    {
        var database = new WorkloadSelector("server-1", "sonarr-db");

        var status = StatusResolver.ForApp(
        [
            (Selector, Inventory(true), Workload(WorkloadState.Running, WorkloadHealth.Healthy)),
            (database, Inventory(true), Workload(WorkloadState.Exited, WorkloadHealth.None)),
        ]);

        status.Status.Should().Be(HealthStatus.Down);
    }

    [Theory]
    [InlineData(WorkloadState.Exited, WorkloadHealth.None, false, "Exited (1) 2 minutes ago")]
    [InlineData(WorkloadState.Running, WorkloadHealth.Unhealthy, true, "Healthcheck failing")]
    public void should_only_degrade_the_app_when_an_optional_workload_fails(WorkloadState state, WorkloadHealth health, bool withCriticalWorkload, string reason)
    {
        var scanner = new WorkloadSelector("server-1", "scanner", MemberRole.Optional);
        var critical = (Selector, Inventory(true), Workload(WorkloadState.Running, WorkloadHealth.Healthy));
        var optional = (scanner, Inventory(true), Workload(state, health));

        var status = StatusResolver.ForApp(withCriticalWorkload ? [critical, optional] : [optional]);

        status.Should().Be(new ResolvedStatus(HealthStatus.Degraded, reason));
    }

    [Fact]
    public void should_put_the_stack_down_and_name_the_app_when_a_critical_member_is_down()
    {
        var status = StatusResolver.ForStack(
        [
            (new StackMember("sonarr", MemberRole.Critical), "Sonarr", HealthStatus.Down),
            (new StackMember("jellyfin", MemberRole.Critical), "Jellyfin", HealthStatus.Healthy),
        ]);

        status.Should().Be(new ResolvedStatus(HealthStatus.Down, "Sonarr is down"));
    }

    [Fact]
    public void should_only_degrade_the_stack_when_an_optional_member_is_down()
    {
        var status = StatusResolver.ForStack(
        [
            (new StackMember("sonarr", MemberRole.Critical), "Sonarr", HealthStatus.Healthy),
            (new StackMember("recyclarr", MemberRole.Optional), "Recyclarr", HealthStatus.Down),
        ]);

        status.Status.Should().Be(HealthStatus.Degraded);
        status.Reason.Should().Contain("Recyclarr");
    }

    [Fact]
    public void should_be_healthy_when_every_member_is_healthy()
    {
        StatusResolver.ForStack([(new StackMember("sonarr", MemberRole.Critical), "Sonarr", HealthStatus.Healthy)])
            .Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void should_degrade_the_stack_when_a_critical_member_is_paused()
    {
        StatusResolver.ForStack([(new StackMember("sonarr", MemberRole.Critical), "Sonarr", HealthStatus.Paused)])
            .Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void should_be_unknown_when_the_stack_has_no_member()
    {
        StatusResolver.ForStack([]).Status.Should().Be(HealthStatus.Unknown);
    }

    [Fact]
    public void should_be_unknown_when_no_member_has_data()
    {
        StatusResolver.ForStack([(new StackMember("sonarr", MemberRole.Critical), "Sonarr", HealthStatus.Unknown)])
            .Status.Should().Be(HealthStatus.Unknown);
    }

    private static Workload Workload(WorkloadState state, WorkloadHealth health)
    {
        return new Workload("server-1", "abc", "sonarr", "sonarr:latest", state, health, "Exited (1) 2 minutes ago", DateTimeOffset.UnixEpoch, null, new Dictionary<string, string>());
    }

    private static EnvironmentInventory Inventory(bool reachable)
    {
        return new EnvironmentInventory("server-1", reachable, reachable ? null : "timeout", [], null);
    }
}
