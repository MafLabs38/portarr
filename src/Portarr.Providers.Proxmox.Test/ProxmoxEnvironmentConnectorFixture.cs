using Portarr.Core.Catalog;

namespace Portarr.Providers.Proxmox.Test;

/// <summary>The <c>proxmox:</c> section of an environment (D-128): a node, its API token's id, the variable of its secret, the pin of its certificate.</summary>
public sealed class ProxmoxEnvironmentConnectorFixture
{
    private const string Fingerprint = "B4:F2:77:84:C8:9B:A0:C6:12:62:10:C8:97:09:EB:36:70:5D:AF:60:92:7B:55:36:7A:2F:47:52:76:FD:C3:65";

    private static CatalogParseResult Parse(string section) =>
        CatalogParser.Parse($"environments:\n  - id: hv\n    name: Hypervisor\n    runtime: proxmox\n    proxmox: {section}\n", [new ProxmoxEnvironmentConnector()]);

    [Fact]
    public void should_read_a_node_with_its_token_and_pin_and_default_the_node_to_the_environment()
    {
        var parsed = Parse($"{{ url: \"https://10.0.0.30:8006/ignored?x=1\", tokenId: \"portarr@pve!readonly\", tokenEnv: PORTARR_PROXMOX_TOKEN, fingerprint: \"{Fingerprint.ToLowerInvariant()}\" }}");

        parsed.Errors.Should().BeEmpty();
        var endpoint = parsed.Catalog.Environments.Single().Proxmox()!;
        endpoint.Url.Should().Be(new Uri("https://10.0.0.30:8006/api2/json/"));
        endpoint.Node.Should().Be("hv");
        endpoint.TokenId.Should().Be("portarr@pve!readonly");
        endpoint.TokenVariable.Should().Be("PORTARR_PROXMOX_TOKEN");
        endpoint.Fingerprint.Should().Be(Fingerprint);
    }

    [Theory]
    [InlineData("{ url: \"http://10.0.0.30:8006\", tokenId: \"a@pve!t\", tokenEnv: PORTARR_PROXMOX_TOKEN }", "proxmox.url must be the node's https address")]
    [InlineData("{ url: \"https://user:pass@10.0.0.30:8006\", tokenId: \"a@pve!t\", tokenEnv: PORTARR_PROXMOX_TOKEN }", "proxmox.url must be the node's https address")]
    [InlineData("{ url: \"https://10.0.0.30:8006\", tokenId: \"a@pve\", tokenEnv: PORTARR_PROXMOX_TOKEN }", "proxmox.tokenId must be the API token's id")]
    [InlineData("{ url: \"https://10.0.0.30:8006\", tokenId: \"a@pve!t\" }", "proxmox.tokenEnv must name the environment variable")]
    [InlineData("{ url: \"https://10.0.0.30:8006\", tokenId: \"a@pve!t\", tokenEnv: DATABASE_PASSWORD }", "must be the name of an environment variable starting with PORTARR_PROXMOX_")]
    [InlineData("{ url: \"https://10.0.0.30:8006\", tokenId: \"a@pve!t\", tokenEnv: PORTARR_PROXMOX_TOKEN, fingerprint: \"AB:CD\" }", "proxmox.fingerprint must be the certificate's SHA-256")]
    [InlineData("{ url: \"https://10.0.0.30:8006\", node: \"../x\", tokenId: \"a@pve!t\", tokenEnv: PORTARR_PROXMOX_TOKEN }", "proxmox.node must be the node's name")]
    public void should_refuse_a_section_that_would_send_the_token_somewhere_else_or_name_no_variable(string section, string message)
    {
        Parse(section).Errors.Should().ContainSingle().Which.Should().Contain(message);
    }

    [Fact]
    public void should_make_the_node_one_machine_with_a_runtime_so_that_its_agent_and_its_vms_attach()
    {
        var connector = new ProxmoxEnvironmentConnector();

        connector.IsRuntime.Should().BeTrue();
        connector.SingleMachine.Should().BeTrue();
        connector.Section.Should().Be("proxmox");
    }
}
