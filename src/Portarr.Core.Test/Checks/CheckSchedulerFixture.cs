using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Status;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Checks;

public sealed class CheckSchedulerFixture : CoreTest<CheckScheduler>
{
    private static readonly Uri Target = new("https://app.homelab.example/");

    public CheckSchedulerFixture()
    {
        var store = Mocker.Use<ICheckStore>(new CheckStore());
        var definition = CheckDefinition.Default;
        var app = new CatalogApp("app", "App", AppType.Web, null, null, [], [], [], definition);

        // The fleet reads the store, like the real one: a recorded result gives the check a state.
        Mocker.Get<IFleetService>().GetApps().Returns(_ =>
        [
            new AppView(app, new ResolvedStatus(HealthStatus.Unknown, string.Empty), null, null, [], [], null, new AppCheckView(definition, Target, store.Get("app"), new ResolvedStatus(HealthStatus.Unknown, string.Empty))),
        ]);
        Mocker.Get<IHttpCheckRunner>().RunAsync(Target, definition, Arg.Any<CancellationToken>())
            .Returns(_ => new CheckResult(Clock.GetUtcNow(), 200, TimeSpan.FromMilliseconds(5), null, null));
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
    public async Task should_record_an_invalid_response_when_the_runner_throws_instead_of_cancelling_the_tick()
    {
        Mocker.Get<IHttpCheckRunner>().RunAsync(Target, Arg.Any<CheckDefinition>(), Arg.Any<CancellationToken>())
            .Returns<CheckResult>(_ => throw new InvalidOperationException("a bug in a runner"));

        var run = () => Subject.RunDueAsync(TestContext.Current.CancellationToken);

        await run.Should().NotThrowAsync();
        Mocker.Get<ICheckStore>().Get("app")!.History.Should().ContainSingle().Which.Error.Should().Be(CheckErrors.InvalidResponse);
    }
}
