using Portarr.Core.Backups;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;
using Portarr.Mcp.Digest;

namespace Portarr.Mcp.Test;

/// <summary>What the MCP tools tell an assistant (V5): the same verdicts as the pages, worst first, no secret.</summary>
public sealed class FleetDigestFixture
{
    private static Overview OverviewOfADownAppAndAnUnreachableCluster() => new FleetBuilder()
        .With("vm-1", FleetBuilder.Container("vm-1", "sonarr", WorkloadState.Exited, new WorkloadMount(MountKind.Bind, "/srv/sonarr", "/config")))
        .Unreachable("cluster")
        .Digest.GetOverview();

    [Fact]
    public void should_put_what_is_down_first_and_name_it_with_its_reason()
    {
        var overview = OverviewOfADownAppAndAnUnreachableCluster();

        overview.Apps.Should().Match<AppCounts>(a => a.Total == 2 && a.Down == 1 && a.Unknown == 1);
        overview.NeedsAttention.Select(f => f.Kind).Should().StartWith(["app-down", "environment-unreachable"]);
        overview.NeedsAttention[0].Subjects.Should().ContainSingle().Which.Should().StartWith("Sonarr (Exited");
    }

    [Fact]
    public void should_say_which_data_is_missing_and_which_critical_data_has_no_offsite_copy()
    {
        var overview = OverviewOfADownAppAndAnUnreachableCluster();

        overview.NeedsAttention.Should().ContainSingle(f => f.Kind == "data-missing").Which.Subjects.Should().Equal("Sonarr config (critical)");
        overview.NeedsAttention.Should().ContainSingle(f => f.Kind == "critical-without-offsite").Which.Subjects.Should().Equal("Sonarr config");
        overview.Data.Should().Be(new DataCounts(1, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0));
    }

    [Fact]
    public void should_show_the_connection_of_each_environment_and_where_a_vm_is_hosted()
    {
        var overview = OverviewOfADownAppAndAnUnreachableCluster();

        overview.Environments.Single(e => e.Id == "cluster").Should().Match<EnvironmentLine>(e => e.Connection == "unreachable" && e.Error == "timeout");
        overview.Environments.Single(e => e.Id == "vm-1").HostedOn.Should().Be("Hypervisor");
    }

    [Fact]
    public void should_tell_where_an_app_runs_up_to_the_hypervisor_without_url_credentials()
    {
        var fleet = new FleetBuilder().With("vm-1", FleetBuilder.Container("vm-1", "sonarr"));
        fleet.Metrics["vm-1"] = new EnvironmentMetrics(
            "vm-1",
            [new MachineView(new MachineSample("vm-1", "VM 1", "docker-host", 4, 8L << 30, 1, 2L << 30, UsageScope.Containers), [], "hypervisor")],
            new Dictionary<string, WorkloadUsageSample> { ["sonarr"] = new("sonarr", 0.25, 1L << 30) },
            FleetBuilder.Now,
            null);

        var app = fleet.Digest.GetApp("SONARR")!;

        app.Urls.Should().ContainSingle().Which.Href.Should().Be("https://sonarr.homelab.example/");
        app.Workloads.Should().ContainSingle().Which.Should().Match<AppWorkloadLine>(w =>
            w.Machine == "vm-1" && w.HostedOn == "Hypervisor" && w.CpuCores == 0.25 && w.MemoryGiB == 1);
        app.Stack.Should().Be("Media");
    }

    [Fact]
    public void should_place_a_node_on_its_hypervisor_and_list_top_consumers()
    {
        var fleet = new FleetBuilder().With("cluster", FleetBuilder.Pod("shop-abc"));
        fleet.Metrics["cluster"] = new EnvironmentMetrics(
            "cluster",
            [new MachineView(
                new MachineSample("node-1", "node-1", "kubernetes-node", 4, 16L << 30, 2, 4L << 30, UsageScope.Host, Ready: true),
                [new MachinePoint(FleetBuilder.Now.AddMinutes(-5), 1, 3L << 30), new MachinePoint(FleetBuilder.Now, 3, 4L << 30)],
                "hypervisor")],
            new Dictionary<string, WorkloadUsageSample> { ["shop/shop-abc"] = new("shop/shop-abc", 1.5, 2L << 30) },
            FleetBuilder.Now,
            null);

        var machine = fleet.Digest.GetInfrastructure("cluster").Single().Machines.Single();

        machine.HostedOn.Should().Be("Hypervisor");
        machine.Cpu.Should().Be(new UsageLine(2, 1, 3, 4, 50));
        machine.Memory.NowPercent.Should().Be(25);
        machine.TopConsumers.Should().ContainSingle().Which.Should().BeEquivalentTo(new ConsumerLine("shop-abc", ["shop"], 1.5, 2));
    }

    [Fact]
    public void should_find_workloads_by_image_or_app_and_never_expose_labels()
    {
        var fleet = new FleetBuilder().With("cluster", FleetBuilder.Pod("shop-abc")).With("vm-1", FleetBuilder.Container("vm-1", "sonarr"));

        var matches = fleet.Digest.FindWorkloads("ghcr.io/example");

        matches.Should().ContainSingle().Which.Should().Match<WorkloadMatch>(w => w.Name == "shop-abc" && w.Machine == "node-1" && w.Apps.Single() == "Shop");
        fleet.Digest.FindWorkloads("sonarr").Should().ContainSingle();
    }

    [Fact]
    public void should_cap_subjects_and_say_how_many_more()
    {
        var subjects = FleetDigest.Cap(Enumerable.Range(1, 12).Select(i => $"app-{i}"));

        subjects.Should().HaveCount(FleetDigest.MaxSubjects + 1);
        subjects[^1].Should().Be("and 2 more");
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(10, 10, false)]
    [InlineData(11, 10, true)]
    public void should_cap_only_beyond_the_maximum_and_say_one_more_when_it_is_one(int count, int kept, bool capped)
    {
        var subjects = FleetDigest.Cap(Enumerable.Range(1, count).Select(i => $"app-{i}"));

        subjects.Take(kept).Should().HaveCount(kept);
        (subjects.Count == kept + 1 && subjects[^1].StartsWith("and ", StringComparison.Ordinal)).Should().Be(capped);
        if (capped)
        {
            subjects[^1].Should().Be("and 1 more");
        }
    }

    [Theory]
    [InlineData("https://user:pass@app.homelab.example:8443/path?token=abc#x", "https://app.homelab.example:8443/path")]
    [InlineData("http://10.0.0.10:8096/", "http://10.0.0.10:8096/")]
    [InlineData("https://app.homelab.example", "https://app.homelab.example/")]
    [InlineData("https://app.homelab.example/a%20b?x=1", "https://app.homelab.example/a b")]
    [InlineData("https://user@app.homelab.example/", "https://app.homelab.example/")]
    [InlineData("https://app.homelab.example/#only-a-fragment", "https://app.homelab.example/")]
    public void should_strip_credentials_query_and_fragment_from_urls(string url, string expected)
    {
        SafeUrl.Of(new Uri(url)).Should().Be(expected);
    }

    [Fact]
    public void should_say_nothing_of_a_url_that_is_not_absolute()
    {
        SafeUrl.Of(new Uri("/relative/path", UriKind.Relative)).Should().BeEmpty();
    }

    [Fact]
    public void should_report_a_tool_that_is_not_set_up_as_a_warning_and_a_pending_offsite_copy_as_information()
    {
        var now = FleetBuilder.Now;
        var fleet = new FleetBuilder
        {
            CatalogText = FleetBuilder.Catalog.Replace("offsite: {}", "offsite: { backrest: { plan: sonarr-offsite } }", StringComparison.Ordinal),
        };

        // Backrest is read on vm-1 and knows the off-site plan, which never ran; the local plan is not in the tool.
        fleet.Backups = new BackupMonitorState(new Dictionary<string, SourceReading>
        {
            ["backrest@vm-1"] = new("backrest", "vm-1", [new BackupJob("sonarr-offsite", "sonarr-offsite", "0 3 * * *", ["b2"], null)], null, now),
        });

        var overview = fleet.Digest.GetOverview();

        overview.NeedsAttention.Should().ContainSingle(f => f.Kind == "critical-offsite-pending").Which.Should().Match<Finding>(f => f.Level == "info" && f.Subjects.Single() == "Sonarr config");
        overview.NeedsAttention.Select(f => f.Kind).Should().NotContain("critical-without-offsite", "the off-site job exists: it is pending, not a gap");
        overview.Data.Should().Match<DataCounts>(d => d.Waiting == 0 && d.Missing == 1 && d.CriticalOffsitePending == 1);
    }

    [Fact]
    public void should_report_a_backup_tool_that_is_not_configured()
    {
        var overview = new FleetBuilder
        {
            CatalogText = FleetBuilder.Catalog.Replace("offsite: {}", "offsite: { backrest: { plan: sonarr-offsite } }", StringComparison.Ordinal).Replace("importance: critical", "importance: important", StringComparison.Ordinal),
            Backups = new BackupMonitorState(new Dictionary<string, SourceReading>
            {
                ["backrest@vm-1"] = new("backrest", "vm-1", [], "no-connector", FleetBuilder.Now, NotSetUp: true),
            }),
        }.Digest.GetOverview();

        overview.NeedsAttention.Should().ContainSingle(f => f.Kind == "backup-tool-not-set-up").Which.Level.Should().Be("warning");
        overview.Data.NotSetUp.Should().Be(1);
        overview.Data.Missing.Should().Be(0, "a tool nobody set up is not a missing copy");
    }
}
