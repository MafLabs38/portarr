using Portarr.Core.Catalog;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Catalog;

public sealed class CatalogWorkstationParserFixture
{
    private const string Servers = """
        environments:
          - { id: server-1, name: Server 1 }
        mcpServers:
          - { id: memory, transport: http, http: { url: "https://memory.homelab.example/mcp" } }
          - { id: github, transport: stdio, stdio: { command: npx } }
        """;

    private static IReadOnlyList<string> Errors(string workstations) => TestCatalog.Parse(Servers + "\nworkstations:\n" + workstations).Errors;

    [Fact]
    public void should_read_a_workstation_with_what_is_wanted_on_it()
    {
        var result = TestCatalog.Parse(Servers + """

            workstations:
              - id: dev-laptop
                name: Dev laptop
                os: Windows
                clients: [claude-code, cursor]
                mcp:
                  - memory
                  - { server: github, clients: [claude-code], scope: project }
                notes: The machine I work on.
              - id: dev-vm
                os: linux
                environment: server-1
                clients: [claude-code]
            """);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        var laptop = result.Catalog.WorkstationList[0];
        laptop.Name.Should().Be("Dev laptop");
        laptop.Os.Should().Be("windows");
        laptop.Clients.Should().Equal("claude-code", "cursor");
        laptop.Notes.Should().Be("The machine I work on.");
        laptop.Wanted.Should().HaveCount(2);
        laptop.Wanted[0].Should().BeEquivalentTo(new WantedServer("memory", ["claude-code", "cursor"], null));
        laptop.Wanted[1].Should().BeEquivalentTo(new WantedServer("github", ["claude-code"], "project"));
        laptop.ClientsWanting("memory").Should().Equal("claude-code", "cursor");
        laptop.ClientsWanting("nothing").Should().BeEmpty();

        var vm = result.Catalog.WorkstationList[1];
        vm.Name.Should().Be("dev-vm");
        vm.EnvironmentId.Should().Be("server-1");
        vm.Wanted.Should().BeEmpty();
    }

    [Fact]
    public void should_have_no_workstation_without_the_section()
    {
        TestCatalog.Parse(Servers).Catalog.WorkstationList.Should().BeEmpty();
    }

    [Theory]
    [InlineData("  - { id: pc, clients: [cursor] }", "os must be windows, linux, macos")]
    [InlineData("  - { id: pc, os: amiga, clients: [cursor] }", "os must be")]
    [InlineData("  - { id: pc, os: linux }", "clients must list the assistants")]
    [InlineData("  - { id: pc, os: linux, clients: [] }", "clients must list the assistants")]
    [InlineData("  - { id: pc, os: linux, clients: [notepad] }", "unknown client 'notepad'")]
    [InlineData("  - { id: pc, os: linux, clients: [cursor, cursor] }", "lists 'cursor' twice")]
    [InlineData("  - { id: pc, os: linux, clients: [cursor], environment: ghost }", "environment 'ghost' is not an environment")]
    [InlineData("  - { id: pc, os: linux, clients: [cursor], mcp: [ghost] }", "mcp[0] 'ghost' is not an MCP server")]
    [InlineData("  - { id: pc, os: linux, clients: [cursor], mcp: [memory, memory] }", "mcp[1] lists 'memory' twice")]
    [InlineData("  - { id: pc, os: linux, clients: [cursor], mcp: [{ server: memory, clients: [zed] }] }", "which is not a client of this workstation")]
    [InlineData("  - { id: pc, os: linux, clients: [cursor], mcp: [{ server: memory, clients: [] }] }", "must list at least one client")]
    [InlineData("  - { id: pc, os: linux, clients: [cursor], mcp: [{ server: memory, scope: machine }] }", "scope must be")]
    [InlineData("  - { id: Bad_Id, os: linux, clients: [cursor] }", "must be lowercase letters")]
    public void should_refuse_an_invalid_workstation(string workstation, string expected)
    {
        Errors(workstation).Should().Contain(error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void should_refuse_the_same_id_twice()
    {
        Errors("  - { id: pc, os: linux, clients: [cursor] }\n  - { id: pc, os: windows, clients: [cursor] }").Should().ContainSingle().Which.Should().Contain("used twice");
    }

    [Fact]
    public void should_refuse_an_unknown_key_in_a_wanted_entry()
    {
        TestCatalog.Parse(Servers + "\nworkstations:\n  - { id: pc, os: linux, clients: [cursor], mcp: [{ server: memory, secret: x }] }").IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_keep_workstations_out_of_the_sections_an_api_key_or_the_chat_can_edit()
    {
        Portarr.Core.Catalog.Editing.CatalogSections.All.Should().NotContain("workstations");
    }
}
