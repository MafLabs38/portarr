using Portarr.Test.Common;

namespace Portarr.Core.Test.Mcp;

/// <summary>What the definition of a server says about itself.</summary>
public sealed class McpServerDefinitionFixture
{
    private static bool Unpinned(string command, string args) =>
        TestCatalog.Parse($"mcpServers:\n  - {{ id: s, transport: stdio, stdio: {{ command: {command}, args: {args} }} }}").Catalog.McpServerList.Single().RunsAnUnpinnedPackage;

    [Theory]
    [InlineData("npx", "[\"-y\", \"@example/pkg\"]", true)]
    [InlineData("npx", "[\"-y\", \"pkg\"]", true)]
    [InlineData("npx", "[\"-y\", \"pkg@latest\"]", true)]
    [InlineData("uvx", "[\"pkg\"]", true)]
    [InlineData("npx", "[\"-y\", \"@example/pkg@1.4.2\"]", false)]
    [InlineData("uvx", "[\"pkg@1.0.0\"]", false)]
    [InlineData("uvx", "[\"mcp-server-fetch==1.2.0\"]", false)]
    [InlineData("uvx", "[\"mcp-server-fetch>=1.2.0\"]", true)]
    [InlineData("uvx", "[\"--from\", \"pkg==1.2.0\", \"cmd\"]", false)]
    [InlineData("uvx", "[\"--from\", \"pkg\", \"cmd\"]", true)]
    [InlineData("node", "[\"server.js\"]", false)]
    [InlineData("cmd", "[\"/c\", \"npx\", \"-y\", \"@example/pkg\"]", true)]
    [InlineData("cmd.exe", "[\"/C\", \"npx.cmd\", \"-y\", \"pkg\"]", true)]
    [InlineData("cmd", "[\"/c\", \"npx\", \"-y\", \"pkg@2.0.0\"]", false)]
    [InlineData("cmd", "[\"/c\", \"dir\"]", false)]
    [InlineData("cmd", "[]", false)]
    public void should_say_whether_a_package_runs_without_a_pinned_version(string command, string args, bool expected)
    {
        Unpinned(command, args).Should().Be(expected);
    }
}
