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
        await Subject.RunDueAsync(TestContext.Current.CancellationToken);
        Clock.Advance(CheckScheduler.Tick);
        await Subject.RunDueAsync(TestContext.Current.CancellationToken);

        Mocker.Get<ICheckStore>().Get("app")!.History.Should().ContainSingle();

        Clock.Advance(CheckDefinition.DefaultInterval);
        await Subject.RunDueAsync(TestContext.Current.CancellationToken);

        Mocker.Get<ICheckStore>().Get("app")!.History.Should().HaveCount(2);
    }
}
