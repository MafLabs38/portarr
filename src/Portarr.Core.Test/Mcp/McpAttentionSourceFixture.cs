using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Mcp;

/// <summary>The MCP servers that are Down, as one "needs attention" finding (D-114): only a server that was really found dead, and with how long.</summary>
public sealed class McpAttentionSourceFixture
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private const string Servers = """
        mcpServers:
          - { id: memory, name: Memory, transport: http, http: { url: "https://memory.homelab.example/mcp" } }
          - { id: github, transport: http, http: { url: "https://github.homelab.example/mcp" } }
        """;

    private static McpAttentionSource Source(string yaml, params McpServerHealth[] health)
    {
        var catalog = Substitute.For<ICatalogService>();
        catalog.Current.Returns(new CatalogState(TestCatalog.Parse(yaml).Catalog, "catalog.yaml", true, [], Now));
        var monitor = Substitute.For<IMcpHealth>();
        monitor.Current.Returns(health.ToDictionary(h => h.ServerId));
        return new McpAttentionSource(catalog, monitor, new FakeTimeProvider(Now));
    }

    private static McpServerHealth Down(string id, int days, bool checkFailed = false) =>
        new(id, McpHealthState.Down, Now.AddDays(-days), Now.AddDays(-days), Now, "timeout", null, checkFailed);

    [Fact]
    public void should_list_the_servers_that_are_down_with_the_time_since_the_longest_first()
    {
        var finding = Source(Servers, Down("github", 2), Down("memory", 12)).GetDownFinding()!;

        finding.Area.Should().Be("mcp");
        finding.Kind.Should().Be("mcp-server-down");
        finding.Count.Should().Be(2);
        finding.Subjects.Should().Equal("Memory (down for 12 d)", "github (down for 2 d)");
    }

    [Fact]
    public void should_say_nothing_of_a_server_that_is_up_asks_for_credentials_could_not_be_checked_or_is_not_monitored()
    {
        var health = new[]
        {
            new McpServerHealth("memory", McpHealthState.Healthy, Now, Now, Now, null, 200),
            new McpServerHealth("github", McpHealthState.AuthenticationRequired, Now, Now, Now, null, 401),
        };

        Source(Servers, health).GetDownFinding().Should().BeNull();
        Source(Servers, Down("memory", 12, checkFailed: true)).GetDownFinding().Should().BeNull("a check Portarr could not run says nothing about the server (D-104)");
        Source(Servers).GetDownFinding().Should().BeNull("a server not checked yet is not down");
        Source("mcpServers:\n  - { id: local, transport: stdio, stdio: { command: npx } }", Down("local", 3)).GetDownFinding().Should().BeNull("a local server is not monitored");
    }

    [Theory]
    [InlineData(0, "1 min")]
    [InlineData(5, "5 min")]
    [InlineData(90, "1 h")]
    [InlineData(60 * 49, "2 d")]
    public void should_write_a_duration_as_short_as_the_list_wants(int minutes, string expected)
    {
        McpAttentionSource.Duration(TimeSpan.FromMinutes(minutes)).Should().Be(expected);
    }
}
