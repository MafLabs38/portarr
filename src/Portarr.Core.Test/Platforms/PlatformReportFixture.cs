using Portarr.Core.Infrastructure;
using Portarr.Core.Maintenance;
using Portarr.Core.Platforms;

namespace Portarr.Core.Test.Platforms;

public sealed class PlatformReportFixture
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly PlatformKnowledge Knowledge = new(
        new Dictionary<string, IReadOnlyList<ReleaseLine>>
        {
            ["k3s"] = [new("1.31", "v1.31.6+k3s1"), new("1.32", "v1.32.2+k3s1"), new("1.33", "v1.33.1+k3s1")],
        },
        new Dictionary<string, IReadOnlyList<SupportCycle>>
        {
            ["ubuntu"] = [new("25.10", new(2027, 1, 1), new(2027, 7, 1)), new("24.04", new(2029, 4, 1), new(2029, 4, 1)), new("20.04", new(2025, 5, 1), new(2025, 5, 1))],
            ["debian"] = [new("13", null, new(2026, 12, 1))],
            ["kubernetes"] = [new("1.32", new(2026, 12, 28), new(2027, 2, 28)), new("1.31", null, new(2026, 10, 28))],
        },
        Now);

    private static MachineSystemEntry Entry(MachineSystem system, string machine = "node-1") => new("env", machine, machine, system);

    private static IReadOnlyList<PlatformStatus> Build(params MachineSystemEntry[] entries) => PlatformReport.Build(entries, Knowledge, Now);

    [Fact]
    public void should_report_a_k3s_node_behind_by_a_patch_and_a_minor_with_its_newer_versions()
    {
        var status = Build(Entry(new MachineSystem(null, null, null, null, "v1.31.4+k3s1"))).Single();

        status.Should().BeEquivalentTo(new
        {
            Kind = "k3s",
            Name = "K3s",
            Version = "v1.31.4+k3s1",
            NewestPatch = "v1.31.6+k3s1",
            NextMinor = "v1.32.2+k3s1",
            EndOfLife = new DateOnly(2026, 10, 28),
            Level = (AttentionLevel?)AttentionLevel.Danger,
        });
    }

    [Fact]
    public void should_only_suggest_the_next_line_never_a_jump_of_two()
    {
        var status = Build(Entry(new MachineSystem(null, null, null, null, "v1.31.6+k3s1"))).Single();

        status.NewestPatch.Should().BeNull();
        status.NextMinor.Should().Be("v1.32.2+k3s1");
    }

    [Fact]
    public void should_say_nothing_about_a_k3s_node_that_is_up_to_date_on_a_supported_line()
    {
        var status = Build(Entry(new MachineSystem(null, null, null, null, "v1.33.1+k3s1"))).Single();

        (status.NewestPatch, status.NextMinor, status.Level).Should().Be(((string?)null, (string?)null, (AttentionLevel?)null));
    }

    [Fact]
    public void should_not_look_up_newer_versions_for_a_kubernetes_that_is_not_k3s()
    {
        var status = Build(Entry(new MachineSystem(null, null, null, null, "v1.32.0+rke2r1"))).Single();

        status.Kind.Should().Be("kubernetes");
        status.Name.Should().Be("Kubernetes");
        (status.NewestPatch, status.NextMinor).Should().Be(((string?)null, (string?)null));
        status.EndOfLife.Should().Be(new DateOnly(2027, 2, 28));
    }

    [Theory]
    [InlineData("20.04", AttentionLevel.Danger)]
    [InlineData("25.10", null)]
    [InlineData("24.04", null)]
    public void should_rate_an_operating_system_by_its_end_of_life(string version, AttentionLevel? expected)
    {
        var status = Build(Entry(new MachineSystem("ubuntu", $"Ubuntu {version}", version, "6.17.0"))).Single();

        status.Kind.Should().Be("os");
        status.Name.Should().Be($"Ubuntu {version}");
        status.Version.Should().Be(version);
        status.Level.Should().Be(expected);
    }

    [Fact]
    public void should_warn_in_the_ninety_days_before_the_end_of_life_and_not_before()
    {
        var debian = Build(Entry(new MachineSystem("debian", "Debian GNU/Linux 13", "13", null))).Single();

        debian.EndOfLife.Should().Be(new DateOnly(2026, 12, 1));
        debian.Level.Should().Be(AttentionLevel.Warning);
        PlatformReport.LevelOf(new DateOnly(2027, 2, 12), false, new DateOnly(2026, 11, 14)).Should().Be(AttentionLevel.Warning);
        PlatformReport.LevelOf(new DateOnly(2027, 2, 13), false, new DateOnly(2026, 11, 14)).Should().BeNull();
        PlatformReport.LevelOf(new DateOnly(2026, 11, 14), false, new DateOnly(2026, 11, 14)).Should().Be(AttentionLevel.Danger);
    }

    [Fact]
    public void should_match_a_point_release_to_its_cycle_but_not_a_longer_number()
    {
        Build(Entry(new MachineSystem("debian", "Debian 13.2", "13.2", null))).Single().EndOfLife.Should().Be(new DateOnly(2026, 12, 1));
        Build(Entry(new MachineSystem("debian", "Debian 130", "130", null))).Single().EndOfLife.Should().BeNull();
    }

    [Fact]
    public void should_report_a_system_without_support_dates_when_the_product_is_unknown_to_the_sources()
    {
        var status = Build(Entry(new MachineSystem("unraid-os", "Unraid OS 7.3 x86_64", "7.3.4", "6.18.58-Unraid"))).Single();

        status.Name.Should().Be("Unraid OS 7.3 x86_64");
        (status.SupportedUntil, status.EndOfLife, status.Level).Should().Be(((DateOnly?)null, (DateOnly?)null, (AttentionLevel?)null));
    }

    [Fact]
    public void should_report_both_the_os_and_the_kubelet_of_a_node_in_a_stable_order()
    {
        var statuses = Build(
            Entry(new MachineSystem("ubuntu", "Ubuntu 25.10", "25.10", "6.17.0", "v1.32.2+k3s1"), "b"),
            Entry(new MachineSystem(null, "Debian 13", "13", null), "a"));

        statuses.Select(s => $"{s.MachineId}/{s.Kind}").Should().Equal("a/os", "b/k3s", "b/os");
    }

    [Fact]
    public void should_say_nothing_for_a_machine_that_says_nothing()
    {
        Build(Entry(new MachineSystem(null, null, null, "6.17.0"))).Should().BeEmpty();
    }

    [Fact]
    public void should_prefer_what_the_host_agent_says_and_keep_the_kubelet_of_the_inventory()
    {
        var infrastructure = new Dictionary<string, EnvironmentMetrics>
        {
            ["env"] = new("env", [new MachineView(Sample("node-1", new MachineSystem(null, "Ubuntu 24.04.3 LTS", null, "6.8.0", "v1.32.2+k3s1")), [])], new Dictionary<string, WorkloadUsageSample>(), Now, null),
        };
        var hosts = new Dictionary<HostMachineKey, HostMetricsView>
        {
            [new("env", "node-1")] = HostView(new MachineSystem("ubuntu", "Ubuntu 25.10", "25.10", "6.17.0")),
        };

        var entry = PlatformReport.SystemsOf(infrastructure, hosts).Single();

        entry.System.Should().Be(new MachineSystem("ubuntu", "Ubuntu 25.10", "25.10", "6.17.0", "v1.32.2+k3s1"));
    }

    [Fact]
    public void should_include_a_host_the_inventory_does_not_list_yet_and_skip_one_that_says_nothing()
    {
        var hosts = new Dictionary<HostMachineKey, HostMetricsView>
        {
            [new("env", "docker-1")] = HostView(new MachineSystem("ubuntu", "Ubuntu 25.10", "25.10", null)),
            [new("env", "mute")] = HostView(null),
        };

        var entries = PlatformReport.SystemsOf(new Dictionary<string, EnvironmentMetrics>(), hosts);

        entries.Should().ContainSingle().Which.Should().Match<MachineSystemEntry>(e => e.MachineId == "docker-1" && e.MachineName == "docker-1");
    }

    [Fact]
    public void should_want_the_k3s_lines_and_the_products_the_machines_run_and_refuse_a_product_that_is_not_an_identifier()
    {
        var (lines, products) = PlatformReport.Wanted(
        [
            Entry(new MachineSystem("Ubuntu", "Ubuntu 25.10", "25.10", null, "v1.32.2+k3s1")),
            Entry(new MachineSystem("../../etc", "x", "1", null)),
            Entry(new MachineSystem("debian", "Debian 13", "13", null, "v1.32.0+rke2r1")),
            Entry(new MachineSystem("unraid-os", "Unraid", "7", null)),
        ]);

        lines.Should().BeEquivalentTo(["k3s"]);
        products.Should().BeEquivalentTo(["ubuntu", "kubernetes", "debian", "unraid-os"]);
    }

    private static MachineSample Sample(string id, MachineSystem system) => new(id, id, "kubernetes-node", 4, 8_000_000_000, null, null, UsageScope.Host, System: system);

    private static HostMetricsView HostView(MachineSystem? system)
    {
        var reading = new HostReading(1, 10, 0, [10], new HostMemory(1, 1, 1, 0, 0, 0), 0.1, null, system);
        return new HostMetricsView(new HostMachineKey("env", "x"), reading, Now, [], null);
    }
}
