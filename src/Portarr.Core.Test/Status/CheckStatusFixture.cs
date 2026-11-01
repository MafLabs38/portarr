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
        StatusResolver.ForCheck(Passed(), Now).Should().Be(new ResolvedStatus(HealthStatus.Healthy, StatusReasons.CheckUp, ["200", "140"]));
    }

    [Theory]
    [InlineData("timeout", "timeout")]
    [InlineData("tls-invalid", "tls-invalid")]
    [InlineData("tls-expired", "certificate-expired")]
    [InlineData("keyword-missing", "keyword-missing")]
    [InlineData("something-new", "invalid-response")]
    public void should_be_down_with_the_code_of_the_failure(string error, string reason)
    {
        StatusResolver.ForCheck(Passed() with { Error = error }, Now).Should().Be(new ResolvedStatus(HealthStatus.Down, reason));
    }

    [Fact]
    public void should_carry_the_number_of_an_http_status()
    {
        StatusResolver.ForCheck(Passed() with { Error = "http-502" }, Now).Should().Be(new ResolvedStatus(HealthStatus.Down, StatusReasons.HttpStatus, ["502"]));
    }

    [Fact]
    public void should_be_unknown_not_down_when_portarr_could_not_run_the_check()
    {
        StatusResolver.ForCheck(Passed() with { Error = CheckErrors.CheckFailed }, Now).Should().Be(new ResolvedStatus(HealthStatus.Unknown, StatusReasons.CheckFailed));
    }

    [Theory]
    [InlineData(6.5, HealthStatus.Degraded, "certificate-expires-in-days", "6")]
    [InlineData(1.5, HealthStatus.Degraded, "certificate-expires-in-one-day", null)]
    [InlineData(0.5, HealthStatus.Degraded, "certificate-expires-today", null)]
    [InlineData(-1, HealthStatus.Down, "certificate-expired", null)]
    [InlineData(14, HealthStatus.Healthy, "check-up", "200")]
    public void should_degrade_within_14_days_of_the_certificate_expiry(double days, HealthStatus status, string reason, string? firstArgument)
    {
        var resolved = StatusResolver.ForCheck(Passed() with { CertificateExpiresAt = Now.AddDays(days) }, Now);

        resolved.Status.Should().Be(status);
        resolved.Reason.Should().Be(reason);
        resolved.Args?.FirstOrDefault().Should().Be(firstArgument);
    }

    [Fact]
    public void should_turn_an_app_down_when_its_critical_check_fails_even_if_its_container_is_up()
    {
        var status = StatusResolver.ForApp([Running()], Check(Passed() with { Error = "http-502" }, critical: true));

        status.Should().Be(new ResolvedStatus(HealthStatus.Down, StatusReasons.HttpStatus, ["502"]));
    }

    [Fact]
    public void should_only_degrade_an_app_when_its_non_critical_check_fails()
    {
        var status = StatusResolver.ForApp([Running()], Check(Passed() with { Error = "timeout" }, critical: false));

        status.Should().Be(new ResolvedStatus(HealthStatus.Degraded, "timeout"));
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
        StatusResolver.ForApp([], Check(null, critical: true)).Reason.Should().Be(StatusReasons.NotChecked);
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
