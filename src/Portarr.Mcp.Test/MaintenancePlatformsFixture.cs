using Portarr.Core.Maintenance;
using Portarr.Core.Platforms;

namespace Portarr.Mcp.Test;

/// <summary>What the maintenance tool tells an assistant about the platforms the machines run (D-119).</summary>
public sealed class MaintenancePlatformsFixture
{
    private static readonly PlatformStatus[] Platforms =
    [
        new("hypervisor", "hypervisor", "Hypervisor", "os", "Ubuntu 20.04", "20.04", null, null, null, new DateOnly(2025, 5, 1), AttentionLevel.Danger),
        new("cluster", "node-1", "node-1", "k3s", "K3s", "v1.31.4+k3s1", "v1.31.6+k3s1", "v1.32.2+k3s1", null, new DateOnly(2027, 10, 28), AttentionLevel.Info),
        new("cluster", "node-2", "node-2", "os", "Unraid OS 7.3", "7.3.4", null, null, null, null, null),
    ];

    [Fact]
    public void should_list_the_platforms_with_their_newer_versions_and_support_days()
    {
        var digest = new FleetBuilder { Platforms = Platforms }.Digest.GetMaintenance("platforms");

        digest.Platforms.Select(p => $"{p.Machine}|{p.Kind}|{p.Version}|{p.NewestPatchOfMinor}|{p.NextMinor}|{p.EndOfLife}|{p.Level}").Should().Equal(
            "Hypervisor|os|20.04|||2025-05-01|danger",
            "node-1|k3s|v1.31.4+k3s1|v1.31.6+k3s1|v1.32.2+k3s1|2027-10-28|info",
            "node-2|os|7.3.4||||");
        digest.Platforms[1].Environment.Should().Be("Cluster", "the environment is shown by its name");
    }

    [Theory]
    [InlineData("code")]
    [InlineData("images")]
    public void should_leave_the_platforms_out_of_the_other_sections(string section)
    {
        new FleetBuilder { Platforms = Platforms }.Digest.GetMaintenance(section).Platforms.Should().BeEmpty();
    }

    [Fact]
    public void should_include_the_platforms_in_the_whole_report()
    {
        new FleetBuilder { Platforms = Platforms }.Digest.GetMaintenance("all").Platforms.Should().HaveCount(3);
    }
}
