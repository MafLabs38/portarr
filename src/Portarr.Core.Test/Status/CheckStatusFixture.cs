using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Status;
using Portarr.Core.Workloads;

namespace Portarr.Core.Test.Status;

/// <summary>Status of a check (D-50, D-52) and how it combines with the workloads of its app.</summary>
public sealed class CheckStatusFixture
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri Target = new("https://app.homelab.example/");
    private static readonly WorkloadSelector Selector = new("server-1", "app");

    [Fact]
    public void should_be_healthy_with_the_status_and_latency()
    {
        StatusResolver.ForCheck(Passed(), Now).Should().Be(new ResolvedStatus(HealthStatus.Healthy, "HTTP 200 in 140 ms"));
    }

    [Theory]
    [InlineData("http-502", "HTTP 502")]
    [InlineData("timeout", "Timeout")]
    [InlineData("tls-invalid", "Invalid certificate")]
    [InlineData("keyword-missing", "Keyword not found")]
    public void should_be_down_with_the_reason_of_the_failure(string error, string reason)
    {
        StatusResolver.ForCheck(Passed() with { Error = error }, Now).Should().Be(new ResolvedStatus(HealthStatus.Down, reason));
    }

    [Theory]
    [InlineData(6.5, HealthStatus.Degraded, "Certificate expires in 6 days")]
    [InlineData(0.5, HealthStatus.Degraded, "Certificate expires today")]
    [InlineData(-1, HealthStatus.Down, "Certificate expired")]
    [InlineData(14, HealthStatus.Healthy, "HTTP 200 in 140 ms")]
    public void should_degrade_within_14_days_of_the_certificate_expiry(double days, HealthStatus status, string reason)
    {
        StatusResolver.ForCheck(Passed() with { CertificateExpiresAt = Now.AddDays(days) }, Now).Should().Be(new ResolvedStatus(status, reason));
    }

    [Fact]
    public void should_turn_an_app_down_when_its_critical_check_fails_even_if_its_container_is_up()
    {
        var status = StatusResolver.ForApp([Running()], Check(Passed() with { Error = "http-502" }, critical: true));

        status.Should().Be(new ResolvedStatus(HealthStatus.Down, "HTTP 502"));
    }

    [Fact]
    public void should_only_degrade_an_app_when_its_non_critical_check_fails()
    {
        var status = StatusResolver.ForApp([Running()], Check(Passed() with { Error = "timeout" }, critical: false));

        status.Should().Be(new ResolvedStatus(HealthStatus.Degraded, "Timeout"));
    }

    [Fact]
    public void should_keep_the_workload_reason_when_the_container_is_worse_than_the_check()
    {
        var exited = (Selector, Inventory(), Container(WorkloadState.Exited, "Exited (1) 2 minutes ago"));

        StatusResolver.ForApp([exited], Check(Passed(), critical: true)).Reason.Should().Be("Exited (1) 2 minutes ago");
    }

    [Fact]
    public void should_take_the_check_as_the_status_of_an_app_without_workloads()
    {
        StatusResolver.ForApp([], Check(Passed(), critical: true)).Status.Should().Be(HealthStatus.Healthy);
        StatusResolver.ForApp([], Check(null, critical: true)).Reason.Should().Be("Not checked yet");
    }

    [Fact]
    public void should_ignore_a_check_without_result_yet_so_a_restart_does_not_blink_apps_to_unknown()
    {
        StatusResolver.ForApp([Running()], Check(null, critical: true)).Status.Should().Be(HealthStatus.Healthy);
    }

    private static CheckResult Passed() => new(Now, 200, TimeSpan.FromMilliseconds(140), null, null);

    private static AppCheckView Check(CheckResult? latest, bool critical)
    {
        var definition = CheckDefinition.Default with { Critical = critical };
        var state = latest is null ? null : new CheckState("app", Target, definition, [latest]);
        return new AppCheckView(definition, Target, state, StatusResolver.ForCheck(latest, Now));
    }

    private static (WorkloadSelector, EnvironmentInventory?, Workload?) Running() => (Selector, Inventory(), Container(WorkloadState.Running, "Up 2 days"));

    private static EnvironmentInventory Inventory() => new("server-1", true, null, [], Now);

    private static Workload Container(WorkloadState state, string text) =>
        new("server-1", "id", "app", "image", state, WorkloadHealth.None, text, Now, null, new Dictionary<string, string>());
}
