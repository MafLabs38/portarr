using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Status;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Checks;

public sealed class CheckSchedulerFixture : CoreTest<CheckScheduler>
{
    private static readonly Uri Target = new("https://app.homelab.example/");

    private readonly List<string> _apps = ["app"];
    private bool _fleetForgetsTheState;

    public CheckSchedulerFixture()
    {
        var store = Mocker.Use<ICheckStore>(new CheckStore());
        var definition = CheckDefinition.Default;

        // The fleet reads the store, like the real one: a recorded result gives the check a state.
        Mocker.Get<IFleetService>().GetApps().Returns(_ => _apps.Select(id => ViewOf(id, definition, _fleetForgetsTheState ? null : store.Get(id))).ToList());
        Mocker.Get<IHttpCheckRunner>().RunAsync(Target, definition, Arg.Any<CancellationToken>())
            .Returns(_ => new CheckResult(Clock.GetUtcNow(), 200, TimeSpan.FromMilliseconds(5), null, null));
    }

    private static AppView ViewOf(string id, CheckDefinition definition, CheckState? state)
    {
        var app = new CatalogApp(id, id, AppType.Web, null, null, [], [], [], definition);
        var unknown = new ResolvedStatus(HealthStatus.Unknown, string.Empty);
        return new AppView(app, unknown, null, null, [], [], null, new AppCheckView(definition, Target, state, unknown));
    }

    [Fact]
    public async Task should_run_a_check_once_per_interval()
    {
        var runner = Mocker.Get<IHttpCheckRunner>();

        await Subject.RunDueAsync(TestContext.Current.CancellationToken);
        await runner.Received(1).RunAsync(Target, Arg.Any<CheckDefinition>(), Arg.Any<CancellationToken>());

        Clock.Advance(CheckScheduler.Tick);
        await Subject.RunDueAsync(TestContext.Current.CancellationToken);
        await runner.Received(1).RunAsync(Target, Arg.Any<CheckDefinition>(), Arg.Any<CancellationToken>());
        Mocker.Get<ICheckStore>().Get("app")!.History.Should().ContainSingle();

        Clock.Advance(CheckDefinition.DefaultInterval);
        await Subject.RunDueAsync(TestContext.Current.CancellationToken);

        await runner.Received(2).RunAsync(Target, Arg.Any<CheckDefinition>(), Arg.Any<CancellationToken>());
        Mocker.Get<ICheckStore>().Get("app")!.History.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_record_the_failure_the_runner_reports_and_keep_probing()
    {
        Mocker.Get<IHttpCheckRunner>().RunAsync(Target, Arg.Any<CheckDefinition>(), Arg.Any<CancellationToken>())
            .Returns(_ => new CheckResult(Clock.GetUtcNow(), 503, TimeSpan.FromMilliseconds(5), CheckErrors.UnexpectedStatus(503), null));

        await Subject.RunDueAsync(TestContext.Current.CancellationToken);

        var recorded = Mocker.Get<ICheckStore>().Get("app")!.History.Should().ContainSingle().Subject;
        recorded.Up.Should().BeFalse();
        recorded.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task should_let_a_cancellation_through_and_record_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        Mocker.Get<IHttpCheckRunner>().RunAsync(Target, Arg.Any<CheckDefinition>(), Arg.Any<CancellationToken>())
            .Returns<CheckResult>(_ =>
            {
                cancelled.Cancel();
                throw new OperationCanceledException(cancelled.Token);
            });

        var run = () => Subject.RunDueAsync(cancelled.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        Mocker.Get<ICheckStore>().Get("app").Should().BeNull();
    }

    [Fact]
    public async Task should_record_a_failed_check_when_the_runner_throws_instead_of_cancelling_the_tick()
    {
        Mocker.Get<IHttpCheckRunner>().RunAsync(Target, Arg.Any<CheckDefinition>(), Arg.Any<CancellationToken>())
            .Returns<CheckResult>(_ => throw new InvalidOperationException("a bug in a runner"));

        var run = () => Subject.RunDueAsync(TestContext.Current.CancellationToken);

        await run.Should().NotThrowAsync();
        Mocker.Get<ICheckStore>().Get("app")!.History.Should().ContainSingle().Which.Error.Should().Be(CheckErrors.CheckFailed);
    }

    [Fact]
    public async Task should_probe_a_changed_check_at_once_instead_of_waiting_for_its_interval()
    {
        var runner = Mocker.Get<IHttpCheckRunner>();
        await Subject.RunDueAsync(TestContext.Current.CancellationToken);

        _fleetForgetsTheState = true; // the catalog changed the check: the fleet has no state for the new one
        await Subject.RunDueAsync(TestContext.Current.CancellationToken);

        await runner.Received(2).RunAsync(Target, Arg.Any<CheckDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_forget_the_results_of_an_app_that_left_the_catalog()
    {
        await Subject.RunDueAsync(TestContext.Current.CancellationToken);
        Mocker.Get<ICheckStore>().Get("app").Should().NotBeNull();

        _apps.Clear();
        await Subject.RunDueAsync(TestContext.Current.CancellationToken);

        Mocker.Get<ICheckStore>().Get("app").Should().BeNull();
    }

    [Fact]
    public async Task should_cap_the_concurrent_probes()
    {
        _apps.Clear();
        _apps.AddRange(Enumerable.Range(1, 10).Select(n => $"app-{n}"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sixRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var counters = new object();
        int running = 0, peak = 0;
        Mocker.Get<IHttpCheckRunner>().RunAsync(Target, Arg.Any<CheckDefinition>(), Arg.Any<CancellationToken>())
            .Returns(_ => Probe());

        async Task<CheckResult> Probe()
        {
            lock (counters)
            {
                peak = Math.Max(peak, ++running);
                if (running == CheckScheduler.MaxConcurrency)
                {
                    sixRunning.TrySetResult();
                }
            }

            await gate.Task;
            lock (counters)
            {
                running--;
            }

            return new CheckResult(Clock.GetUtcNow(), 200, TimeSpan.FromMilliseconds(5), null, null);
        }

        var run = Subject.RunDueAsync(TestContext.Current.CancellationToken);
        await sixRunning.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // A probe over the cap would start right now: a test that nothing happens needs a window. It can only let a broken cap
        // pass by luck, never fail a good one.
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        gate.SetResult();
        await run;

        peak.Should().Be(CheckScheduler.MaxConcurrency);
        await Mocker.Get<IHttpCheckRunner>().Received(10).RunAsync(Target, Arg.Any<CheckDefinition>(), Arg.Any<CancellationToken>());
    }
}
