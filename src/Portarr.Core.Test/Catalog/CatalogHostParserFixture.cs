using Portarr.Core.Catalog;

namespace Portarr.Core.Test.Catalog;

/// <summary>Machines that are VMs on another environment of the catalog: <c>host</c> and <c>nodeHosts</c> (D-59).</summary>
public sealed class CatalogHostParserFixture
{
    [Fact]
    public void should_read_the_host_of_an_environment_and_of_kubernetes_nodes()
    {
        var result = CatalogParser.Parse("""
            environments:
              - { id: hypervisor, docker: { url: "http://10.0.0.10:2375" } }
              - { id: vm-1, host: hypervisor, docker: { url: "http://10.0.0.11:2375" } }
              - id: cluster
                kubernetes: { server: "https://10.0.0.20:6443", nodeHosts: { node-1: hypervisor } }
            """);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        var environments = result.Catalog.Environments;
        environments[0].HostOf("hypervisor").Should().BeNull();
        environments[1].HostOf("vm-1").Should().Be("hypervisor");
        environments[2].HostOf("node-1").Should().Be("hypervisor");
        environments[2].HostOf("node-2").Should().BeNull("a VPS whose hypervisor is not in the catalog has no host");
    }

    [Theory]
    [InlineData("{ id: vm-1, host: nowhere }", "host 'nowhere' is not an environment of the catalog")]
    [InlineData("{ id: vm-1, host: vm-1 }", "cannot be its own host")]
    [InlineData("{ id: vm-1, host: cdn }", "must be a single machine")]
    [InlineData("{ id: vm-1, kubernetes: { server: \"https://10.0.0.20:6443\", nodeHosts: { node-1: nowhere } } }", "host 'nowhere'")]
    [InlineData("{ id: vm-1, kubernetes: { server: \"https://10.0.0.20:6443\", nodeHosts: { node-1: \"\" } } }", "kubernetes.nodeHosts maps node names")]
    public void should_reject_an_invalid_host(string environment, string error)
    {
        var result = CatalogParser.Parse($$"""
            environments:
              - { id: cdn, cloudflare: { accountId: "0123456789abcdef0123456789abcdef" } }
              - {{environment}}
            """);

        result.Errors.Should().ContainSingle().Which.Should().Contain(error);
    }

    [Fact]
    public void should_reject_a_loop_of_hosts()
    {
        var result = CatalogParser.Parse("""
            environments:
              - { id: a, host: b }
              - { id: b, host: a }
            """);

        result.Errors.Should().Contain(e => e.Contains("(a loop)", StringComparison.Ordinal));
    }
}
