using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Configuration;
using Portarr.Core.Infrastructure;
using Portarr.Core.Platforms;

namespace Portarr.Core.Test.Platforms;

public sealed class PlatformMonitorFixture
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly IInfrastructureMonitor _infrastructure = Substitute.For<IInfrastructureMonitor>();
    private readonly IHostMetricsMonitor _hosts = Substitute.For<IHostMetricsMonitor>();
    private readonly IReleaseLineSource _k3s = Substitute.For<IReleaseLineSource>();
    private readonly ISupportLifecycleSource _lifecycle = Substitute.For<ISupportLifecycleSource>();

    public PlatformMonitorFixture()
    {
        _hosts.Current.Returns(new Dictionary<HostMachineKey, HostMetricsView>());
        _k3s.Platform.Returns("k3s");
        _k3s.DisplayName.Returns("K3s");
        _k3s.ReadAsync(Arg.Any<CancellationToken>()).Returns([new ReleaseLine("1.32", "v1.32.2+k3s1")]);
        _lifecycle.ReadAsync("kubernetes", Arg.Any<CancellationToken>()).Returns([new SupportCycle("1.32", null, new DateOnly(2027, 2, 28))]);
        _lifecycle.ReadAsync("ubuntu", Arg.Any<CancellationToken>()).Returns([new SupportCycle("25.10", null, new DateOnly(2027, 7, 1))]);
        _lifecycle.ReadAsync("unraid-os", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SupportCycle>?)null);
        Machines(new MachineSystem("ubuntu", "Ubuntu 25.10", "25.10", "6.17.0", "v1.32.0+k3s1"));
    }

    private PlatformMonitor CreateMonitor() => new(_infrastructure, _hosts, [_k3s], [_lifecycle], Options.Create(new PortarrOptions()), _clock, NullLogger<PlatformMonitor>.Instance);

    private void Machines(params MachineSystem[] systems)
    {
        var views = systems.Select((system, index) => new MachineView(new MachineSample($"m{index}", $"m{index}", "kubernetes-node", 4, 1, null, null, UsageScope.Host, System: system), [])).ToList();
        _infrastructure.Current.Returns(new Dictionary<string, EnvironmentMetrics> { ["env"] = new("env", views, new Dictionary<string, WorkloadUsageSample>(), Start, null) });
    }

    [Fact]
    public async Task should_read_only_what_the_machines_run_and_keep_it()
    {
        var monitor = CreateMonitor();

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        monitor.Current.ReleaseLines["k3s"].Should().ContainSingle().Which.Latest.Should().Be("v1.32.2+k3s1");
        monitor.Current.Cycles.Keys.Should().BeEquivalentTo(["kubernetes", "ubuntu"]);
        monitor.Current.CheckedAt.Should().Be(Start);
    }

    [Fact]
    public async Task should_not_ask_for_the_k3s_lines_when_no_node_runs_k3s()
    {
        Machines(new MachineSystem("ubuntu", "Ubuntu 25.10", "25.10", null, "v1.32.0+rke2r1"));
        var monitor = CreateMonitor();

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        await _k3s.DidNotReceive().ReadAsync(Arg.Any<CancellationToken>());
        monitor.Current.ReleaseLines.Should().BeEmpty();
    }

    [Fact]
    public async Task should_read_again_after_six_hours_and_not_before()
    {
        var monitor = CreateMonitor();
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        _clock.Advance(PlatformMonitor.Interval - TimeSpan.FromMinutes(1));
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        await _k3s.Received(1).ReadAsync(Arg.Any<CancellationToken>());

        _clock.Advance(TimeSpan.FromMinutes(1));
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        await _k3s.Received(2).ReadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_keep_the_last_answer_when_a_source_fails_and_retry_within_the_hour()
    {
        var monitor = CreateMonitor();
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        _k3s.ReadAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<ReleaseLine>>(_ => throw new HttpRequestException("down"));

        _clock.Advance(PlatformMonitor.Interval);
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        monitor.Current.ReleaseLines["k3s"].Should().ContainSingle();

        _clock.Advance(PlatformMonitor.RetryInterval);
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        await _k3s.Received(3).ReadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_not_ask_again_for_a_product_no_source_knows_until_the_interval_has_passed()
    {
        Machines(new MachineSystem("unraid-os", "Unraid OS 7.3", "7.3.4", null));
        var monitor = CreateMonitor();

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        await _lifecycle.Received(1).ReadAsync("unraid-os", Arg.Any<CancellationToken>());
        monitor.Current.Cycles.Should().BeEmpty();
    }

    [Fact]
    public async Task should_never_ask_a_source_for_a_product_that_is_not_an_identifier()
    {
        Machines(new MachineSystem("../../etc", "x", "1", null));

        await CreateMonitor().RunDueAsync(TestContext.Current.CancellationToken);

        await _lifecycle.DidNotReceive().ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_keep_the_cycles_it_has_and_retry_sooner_when_one_product_cannot_be_read()
    {
        var monitor = CreateMonitor();
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        _lifecycle.ReadAsync("ubuntu", Arg.Any<CancellationToken>()).Returns<IReadOnlyList<SupportCycle>?>(_ => throw new HttpRequestException("down"));

        _clock.Advance(PlatformMonitor.Interval);
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        monitor.Current.Cycles.Keys.Should().BeEquivalentTo(["kubernetes", "ubuntu"], "the dead product keeps its last answer and the others are read again");
        monitor.Current.Cycles["ubuntu"].Should().ContainSingle().Which.Cycle.Should().Be("25.10");

        _clock.Advance(PlatformMonitor.RetryInterval);
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        await _lifecycle.Received(3).ReadAsync("ubuntu", Arg.Any<CancellationToken>());
        await _lifecycle.Received(2).ReadAsync("kubernetes", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_keep_the_old_answer_and_retry_sooner_when_one_source_fails_and_a_later_one_does_not_know_the_product()
    {
        var failing = Substitute.For<ISupportLifecycleSource>();
        failing.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<SupportCycle>?>(_ => throw new HttpRequestException("down"));
        var silent = Substitute.For<ISupportLifecycleSource>();
        silent.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SupportCycle>?)null);
        var monitor = new PlatformMonitor(_infrastructure, _hosts, [_k3s], [failing, silent], Options.Create(new PortarrOptions()), _clock, NullLogger<PlatformMonitor>.Instance);

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        _clock.Advance(PlatformMonitor.RetryInterval);
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        await failing.Received(4).ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        monitor.Current.Cycles.Should().BeEmpty();
    }

    [Fact]
    public async Task should_read_nothing_when_the_platform_lookups_are_switched_off()
    {
        var monitor = new PlatformMonitor(_infrastructure, _hosts, [_k3s], [_lifecycle], Options.Create(new PortarrOptions { PlatformUpdates = false }), _clock, NullLogger<PlatformMonitor>.Instance);

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        await _k3s.DidNotReceive().ReadAsync(Arg.Any<CancellationToken>());
        await _lifecycle.DidNotReceive().ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        (monitor.Current.Enabled, monitor.Current.CheckedAt).Should().Be((false, (DateTimeOffset?)null));
    }

    [Fact]
    public async Task should_record_why_a_lookup_failed_and_clear_it_when_it_works_again()
    {
        var monitor = CreateMonitor();
        _lifecycle.ReadAsync("ubuntu", Arg.Any<CancellationToken>()).Returns<IReadOnlyList<SupportCycle>?>(_ => throw new TaskCanceledException());
        _k3s.ReadAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<ReleaseLine>>(_ => throw new HttpRequestException(null, null, System.Net.HttpStatusCode.BadGateway));

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        monitor.Current.Failures.Should().Contain(new KeyValuePair<string, string>("cycles:ubuntu", "timeout")).And.Contain(new KeyValuePair<string, string>("lines:k3s", "http-502"));

        _lifecycle.ReadAsync("ubuntu", Arg.Any<CancellationToken>()).Returns([new SupportCycle("25.10", null, new DateOnly(2027, 7, 1))]);
        _k3s.ReadAsync(Arg.Any<CancellationToken>()).Returns([new ReleaseLine("1.32", "v1.32.2+k3s1")]);
        _clock.Advance(PlatformMonitor.RetryInterval);
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        monitor.Current.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task should_say_a_product_no_source_lists_is_unlisted_and_not_failed()
    {
        Machines(new MachineSystem("unraid-os", "Unraid OS 7.3", "7.3.4", null));
        var monitor = CreateMonitor();

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        monitor.Current.Unlisted.Should().Contain("cycles:unraid-os");
        monitor.Current.Failures.Should().BeEmpty();
    }

    [Fact]
    public async Task should_forget_what_no_machine_needs_any_more()
    {
        var monitor = CreateMonitor();
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        Machines(new MachineSystem("debian", "Debian 13", "13", null));
        _lifecycle.ReadAsync("debian", Arg.Any<CancellationToken>()).Returns([new SupportCycle("13", null, null)]);
        _clock.Advance(PlatformMonitor.Tick);

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        monitor.Current.Cycles.Keys.Should().BeEquivalentTo(["debian"]);
        monitor.Current.ReleaseLines.Should().BeEmpty();
    }

    [Fact]
    public async Task should_read_a_bounded_number_of_products_per_pass_whatever_the_machines_report()
    {
        Machines(Enumerable.Range(0, PlatformMonitor.MaxReadsPerPass + 10).Select(i => new MachineSystem($"os{i:D3}", $"OS {i}", "1", null)).ToArray());
        _lifecycle.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SupportCycle>?)null);
        var monitor = CreateMonitor();

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        await _lifecycle.Received(PlatformMonitor.MaxReadsPerPass).ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        await _lifecycle.Received(PlatformMonitor.MaxReadsPerPass + 10).ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_carry_the_display_name_of_the_platform_the_source_gives()
    {
        var monitor = CreateMonitor();

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        monitor.Current.DisplayNames.Should().Contain("k3s", "K3s");
    }

    [Fact]
    public async Task should_stop_when_asked_to_even_in_the_middle_of_a_read()
    {
        using var cancel = new CancellationTokenSource();
        _k3s.ReadAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<ReleaseLine>>(_ =>
        {
            cancel.Cancel();
            throw new OperationCanceledException(cancel.Token);
        });

        var act = () => CreateMonitor().RunDueAsync(cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
