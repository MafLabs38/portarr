using Portarr.Test.Common;

namespace Portarr.Mcp.Test;

/// <summary>The Markdown map an assistant reads at once (V5, D-69).</summary>
public sealed class KnowledgeBaseFixture
{
    [Fact]
    public void should_nest_vms_under_their_hypervisor_and_list_apps_with_where_they_run()
    {
        var fleet = new FleetBuilder().With("vm-1", FleetBuilder.Container("vm-1", "sonarr")).With("cluster", FleetBuilder.Pod("shop-abc"));

        var markdown = fleet.Knowledge.BuildInfrastructure();

        markdown.Should().Contain("- **Hypervisor** (`hypervisor`, docker, LAN address 10.0.0.10)")
            .And.Contain("\n  - VM: node `node-1` of **Cluster** (`cluster`)")
            .And.Contain("\n  - **VM 1** (`vm-1`, docker, VM on Hypervisor)")
            .And.Contain("| Sonarr (`sonarr`) | Healthy | critical | vm-1/sonarr | public: https://sonarr.homelab.example/ |")
            .And.Contain("cluster/node-1/shop-abc");
    }

    [Fact]
    public void should_never_contain_secrets_tokens_or_connector_endpoints()
    {
        var fleet = new FleetBuilder().With("vm-1", FleetBuilder.Container("vm-1", "sonarr"));

        var markdown = fleet.Knowledge.BuildInfrastructure();

        markdown.Should().NotContain("s3cr3t").And.NotContain("K8S_TOKEN")
            .And.NotContain("do-not-show").And.NotContain(":2375").And.NotContain(":6443");
    }

    [Fact]
    public void should_tell_how_to_reach_the_machines_their_links_and_the_providers()
    {
        var markdown = new FleetBuilder().Knowledge.BuildInfrastructure();

        markdown.Should().Contain("## Access, network and providers")
            .And.Contain("- **Hypervisor** (`hypervisor`)\n  - reach: `ssh hypervisor`")
            .And.Contain("- **Cluster** (`cluster`): Two nodes, one at home and one at a hosting provider.\n  - machine `node-2`: The VPS.\n    - reach: `ssh -p 2222 -i ~/.ssh/vps ubuntu@vps.homelab.example` (key file ~/.ssh/vps)")
            .And.Contain("- `node-1` ⇄ `node-2`: wireguard-tunnel, network 10.99.0.0/24 (`tunnel`): node-1 initiates.")
            .And.Contain("- **Example DNS** (`dns`, dns, console https://dns.example/manager); used by sonarr: The zone homelab.example.")
            .And.NotContain("session=abc");
    }

    [Fact]
    public void should_narrow_the_knowledge_to_one_environment()
    {
        var catalog = TestCatalog.Parse(FleetBuilder.Catalog).Catalog;

        var vm = Knowledge.EnvironmentKnowledgeBuilder.Build(catalog, "vm-1");
        var cluster = Knowledge.EnvironmentKnowledgeBuilder.Build(catalog, "Cluster");

        vm.Environments.Should().ContainSingle().Which.Apps.Should().Equal("sonarr");
        vm.Providers.Should().ContainSingle().Which.Id.Should().Be("dns");
        vm.Links.Should().BeEmpty();
        cluster.Links.Should().ContainSingle().Which.Kind.Should().Be("wireguard-tunnel");
        cluster.Environments.Single().Machines.Single().Access!.Ssh!.Command.Should().Be("ssh -p 2222 -i ~/.ssh/vps ubuntu@vps.homelab.example");
    }
}
