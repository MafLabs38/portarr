using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Capture;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Mcp.Capture;

/// <summary>Two descriptions are the same server whatever they are called and wherever they were seen (V9, D-116).</summary>
public sealed class McpIdentityFixture
{
    private static CapturedServer Remote(string url) => new("x", "http", url, null, [], [], []);

    private static CapturedServer Local(string command, params string[] args) => new("x", "stdio", null, command, args, [], []);

    [Theory]
    [InlineData("https://Memory.Homelab.example/mcp", "https://memory.homelab.example/mcp/")]
    [InlineData("https://memory.homelab.example:443/mcp", "https://memory.homelab.example/mcp")]
    public void should_give_a_remote_server_the_same_identity_whatever_the_spelling_of_its_address(string a, string b)
    {
        McpIdentity.Of(Remote(a)).Should().Be(McpIdentity.Of(Remote(b)));
    }

    [Fact]
    public void should_tell_two_remote_servers_apart_by_path_and_query()
    {
        McpIdentity.Of(Remote("https://a.homelab.example/mcp")).Should().NotBe(McpIdentity.Of(Remote("https://a.homelab.example/other")));
        McpIdentity.Of(Remote("https://a.homelab.example/mcp?x=1")).Should().NotBe(McpIdentity.Of(Remote("https://a.homelab.example/mcp?x=2")));
    }

    [Theory]
    [InlineData("npx", "-y", "@example/github-mcp@1.4.2")]
    [InlineData("npx", "-y", "@example/github-mcp@latest")]
    [InlineData("npx", "@example/github-mcp")]
    [InlineData("/usr/bin/npx", "--yes", "@example/github-mcp@2.0.0", "--flag")]
    [InlineData(@"C:\tools\npx.cmd", "-y", "@example/github-mcp")]
    public void should_identify_a_package_run_through_npx_without_its_version_or_the_flags(string command, params string[] args)
    {
        McpIdentity.Of(Local(command, args)).Should().Be("stdio:package:@example/github-mcp");
    }

    [Fact]
    public void should_not_mistake_the_scope_of_a_package_for_its_version()
    {
        McpIdentity.Of(Local("uvx", "mcp-server-git@1.0")).Should().Be("stdio:package:mcp-server-git");
        McpIdentity.Of(Local("npx", "@scope/pkg")).Should().Be("stdio:package:@scope/pkg");
    }

    [Fact]
    public void should_identify_any_other_program_by_the_program_and_its_arguments()
    {
        McpIdentity.Of(Local("node", "/srv/mcp/index.js")).Should().Be("stdio:node /srv/mcp/index.js");
        McpIdentity.Of(Local("node", "/srv/mcp/index.js")).Should().NotBe(McpIdentity.Of(Local("node", "/srv/other/index.js")));
    }

    [Fact]
    public void should_give_a_definition_of_the_catalog_the_identity_of_what_it_describes()
    {
        var servers = TestCatalog.Parse("""
            mcpServers:
              - { id: memory, transport: http, http: { url: "https://memory.homelab.example/mcp" } }
              - { id: github, transport: stdio, stdio: { command: npx, args: ["-y", "@example/github-mcp@1.4.2"] } }
            """).Catalog.McpServerList;

        McpIdentity.Of(servers[0]).Should().Be(McpIdentity.Of(Remote("https://memory.homelab.example/mcp")));
        McpIdentity.Of(servers[1]).Should().Be(McpIdentity.Of(Local("npx", "@example/github-mcp")));
    }

    [Fact]
    public void should_give_a_short_stable_key()
    {
        var key = McpIdentity.KeyOf("http:https://a.homelab.example/mcp");

        key.Should().HaveLength(12).And.MatchRegex("^[0-9a-f]+$");
        McpIdentity.KeyOf("http:https://a.homelab.example/mcp").Should().Be(key);
    }
}
