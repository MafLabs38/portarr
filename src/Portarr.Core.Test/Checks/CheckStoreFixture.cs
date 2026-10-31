using Portarr.Core.Catalog;
using Portarr.Core.Checks;

namespace Portarr.Core.Test.Checks;

public sealed class CheckStoreFixture
{
    private static readonly Uri Target = new("https://app.homelab.example/");

    private readonly CheckStore _store = new();

    [Fact]
    public void should_keep_the_last_90_results_oldest_first()
    {
        for (var i = 0; i < 100; i++)
        {
            _store.Record("app", Target, CheckDefinition.Default, Result(i));
        }

        var history = _store.Get("app")!.History;

        history.Should().HaveCount(CheckHistory.Capacity);
        history[0].StatusCode.Should().Be(10);
        _store.Get("app")!.Latest!.StatusCode.Should().Be(99);
    }

    [Fact]
    public void should_start_a_new_history_when_the_check_changes_but_not_on_an_identical_reload()
    {
        _store.Record("app", Target, CheckDefinition.Default, Result(1));
        _store.Record("app", Target, CheckDefinition.Default with { Expect = [new(200, 399)] }, Result(2));
        _store.Get("app")!.History.Should().HaveCount(2);

        _store.Record("app", Target, CheckDefinition.Default with { Keyword = "Grafana" }, Result(3));
        _store.Get("app")!.History.Should().ContainSingle();
    }

    [Fact]
    public void should_forget_checks_of_apps_that_are_gone()
    {
        _store.Record("app", Target, CheckDefinition.Default, Result(1));
        _store.Record("other", Target, CheckDefinition.Default, Result(1));

        _store.Retain(new HashSet<string> { "other" });

        _store.Get("app").Should().BeNull();
        _store.Get("other").Should().NotBeNull();
    }

    private static CheckResult Result(int statusCode)
    {
        return new CheckResult(DateTimeOffset.UnixEpoch.AddMinutes(statusCode), statusCode, TimeSpan.FromMilliseconds(5), null, null);
    }
}
