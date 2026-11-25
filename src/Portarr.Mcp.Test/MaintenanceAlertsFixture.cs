using Portarr.Core.Maintenance;
using Portarr.Mcp.Digest;

namespace Portarr.Mcp.Test;

/// <summary>What an assistant reads of a repository's alerts (D-130): enough to act on the worst, never a secret.</summary>
public sealed class MaintenanceAlertsFixture
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static AlertDetail Alert(string source, string id, string? severity, string package = "lib") =>
        new(source, id, severity, $"Flaw {id}", package, "npm", "< 2.0", "2.0.1", $"GHSA-{id}", $"CVE-2026-{id}", "package-lock.json", new Uri($"https://github.com/example/shop/security/{id}"), Now);

    private static RepositoryView Shop(AlertScan dependabot, AlertScan secrets) => new(
        new CodeRepository("example", "shop", new Uri("https://github.com/example/shop"), "public", "C#", null, Now, "main", false, false,
            dependabot, AlertScan.On(AlertCounts.None), secrets, null, 0, null),
        [], [], false);

    [Fact]
    public void should_hand_the_critical_and_high_alerts_to_an_assistant_with_the_version_that_mends_them()
    {
        var dependabot = AlertScan.On([Alert("dependabot", "1", "medium"), Alert("dependabot", "2", "high"), Alert("dependabot", "3", "critical", "minimist")]);

        var digest = new FleetBuilder { Repositories = [Shop(dependabot, AlertScan.On(AlertCounts.None))] }.Digest.GetMaintenance("code");

        var shop = digest.Repositories.Should().ContainSingle().Subject;
        shop.SevereAlerts.Should().NotBeNull();
        shop.SevereAlerts!.Select(a => a.Severity).Should().Equal("critical", "high");
        shop.SevereAlerts[0].Should().Match<AlertLine>(a => a.Package == "minimist" && a.FixedVersion == "2.0.1" && a.Advisory == "CVE-2026-3");
    }

    [Fact]
    public void should_list_an_open_secret_without_any_value_and_at_most_ten_alerts()
    {
        var many = AlertScan.On([.. Enumerable.Range(1, 15).Select(n => Alert("dependabot", n.ToString(System.Globalization.CultureInfo.InvariantCulture), "critical"))]);
        var secret = AlertScan.On([new AlertDetail("secret-scanning", "9", null, "GitHub Personal Access Token", null, null, null, null, null, null, null, null, Now)]);

        var shop = new FleetBuilder { Repositories = [Shop(many, secret)] }.Digest.GetMaintenance("code").Repositories.Should().ContainSingle().Subject;

        shop.SevereAlerts.Should().HaveCount(10);
        var onlySecret = new FleetBuilder { Repositories = [Shop(AlertScan.On(AlertCounts.None), secret)] }.Digest.GetMaintenance("code").Repositories.Single();
        onlySecret.SevereAlerts.Should().ContainSingle().Which.Source.Should().Be("secret-scanning");
    }

    [Fact]
    public void should_say_nothing_of_alerts_when_there_is_no_severe_one()
    {
        var dependabot = AlertScan.On([Alert("dependabot", "1", "low")]);

        var shop = new FleetBuilder { Repositories = [Shop(dependabot, AlertScan.On(AlertCounts.None))] }.Digest.GetMaintenance("code").Repositories.Should().ContainSingle().Subject;

        shop.SevereAlerts.Should().BeNull();
    }
}
