using Portarr.Core.Maintenance;

namespace Portarr.Core.Test.Maintenance;

/// <summary>The alerts a repository describes: one vocabulary whatever the code host sent, and the worst first (D-130).</summary>
public sealed class AlertScanFixture
{
    private static readonly DateTimeOffset Day = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private static AlertDetail Alert(string id, string? severity, DateTimeOffset? at = null, Uri? url = null) =>
        new(AlertSources.Dependabot, id, severity, "title", null, null, null, null, null, null, null, url, at ?? Day);

    [Fact]
    public void should_put_the_worst_first_then_the_newest_and_read_moderate_as_medium()
    {
        var scan = AlertScan.On([
            Alert("low", "low"),
            Alert("none", null),
            Alert("moderate", "Moderate"),
            Alert("critical", "CRITICAL"),
            Alert("old-high", "high", Day.AddDays(-2)),
            Alert("new-high", "high", Day),
        ]);

        scan.Alerts.Select(a => a.Id).Should().Equal("critical", "new-high", "old-high", "moderate", "low", "none");
        scan.Alerts.Select(a => a.Severity).Should().Equal("critical", "high", "high", "medium", "low", null);
        scan.Counts.Total.Should().Be(6);
    }

    [Fact]
    public void should_forget_an_unknown_severity_and_a_link_that_is_not_https()
    {
        var scan = AlertScan.On([
            Alert("a", "informational", url: new Uri("http://host.example/a")),
            Alert("b", "low", url: new Uri("https://host.example/b")),
        ]);

        scan.Alerts.Single(a => a.Id == "a").Should().Match<AlertDetail>(a => a.Severity == null && a.Url == null);
        scan.Alerts.Single(a => a.Id == "b").Url.Should().Be(new Uri("https://host.example/b"));
    }

    [Fact]
    public void should_have_no_alert_to_list_when_it_was_only_counted()
    {
        AlertScan.On(AlertCounts.None).Alerts.Should().BeEmpty();
    }
}
