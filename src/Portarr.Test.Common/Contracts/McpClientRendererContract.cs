using AwesomeAssertions;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;
using Xunit;

namespace Portarr.Test.Common.Contracts;

/// <summary>
/// The contract of an <see cref="IMcpClientRenderer"/> (rule 8, D-113): an id the catalog can name, scopes the catalog knows, operations for every
/// scope and system the client holds, never the value of a variable, and never a quote that would escape the quoting of a command. The test project
/// of an adapter inherits it and supplies the renderer.
/// </summary>
public abstract class McpClientRendererContract
{
    private const string Catalog = """
        mcpServers:
          - id: remote
            transport: http
            http: { url: "https://remote.homelab.example/mcp", headers: { X-Api-Key: { env: PORTARR_CONTRACT_KEY }, Accept: application/json } }
          - id: local
            transport: stdio
            stdio: { command: npx, args: ["-y", "@example/local-mcp@1.0.0"], env: { LOCAL_KEY: { env: PORTARR_CONTRACT_KEY }, LOG_LEVEL: info } }
        """;

    private static readonly string[] QuoteCharacters = ["'", "‘", "’", "‚", "‛"];

    protected abstract IMcpClientRenderer Renderer { get; }

    private static readonly IReadOnlyList<McpServerDefinition> Servers = TestCatalog.Parse(Catalog).Catalog.McpServerList;

    public static TheoryData<string> Systems() => new(McpOperatingSystems.All);

    [Fact]
    public void should_have_an_id_the_catalog_can_name_and_a_name()
    {
        Renderer.Id.Should().MatchRegex("^[a-z][a-z0-9-]*$");
        Renderer.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void should_hold_servers_at_known_scopes_only()
    {
        Renderer.Scopes.Should().NotBeEmpty().And.OnlyHaveUniqueItems().And.OnlyContain(scope => McpScopes.All.Contains(scope));
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void should_install_and_remove_every_server_at_every_scope_it_holds(string os)
    {
        foreach (var server in Servers)
        {
            foreach (var scope in Renderer.Scopes)
            {
                var context = new McpRenderContext(server, scope, os);

                var install = Renderer.Install(context);
                var remove = Renderer.Remove(context);

                install.Operations.Should().NotBeEmpty($"{Renderer.Id} installs {server.Id} at {scope} on {os}");
                install.Operations.Should().OnlyContain(o => o.Kind == McpOperationKinds.Command || o.Kind == McpOperationKinds.File);
                remove.Operations.Should().NotBeEmpty();
                remove.Operations.Should().OnlyContain(o => o.Kind == McpOperationKinds.Command || o.Kind == McpOperationKinds.Remove);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void should_name_the_variable_a_server_reads_and_never_hold_a_value(string os)
    {
        // A definition holds the name of a variable, never a value (D-111): what a renderer writes is the client's syntax for reading that name.
        var text = string.Join('\n', Servers.SelectMany(s => Renderer.Install(new McpRenderContext(s, Renderer.Scopes[0], os)).Operations).Select(o => o.Text));

        text.Should().Contain("PORTARR_CONTRACT_KEY");
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void should_never_put_a_quote_character_inside_the_quotes_of_a_command(string os)
    {
        foreach (var quote in QuoteCharacters)
        {
            var literal = $"it{quote}s";
            var server = TestCatalog.Parse($$"""
                mcpServers:
                  - id: tricky
                    transport: stdio
                    stdio:
                      command: npx
                      args: ["-y", "{{literal}}"]
                """).Catalog.McpServerList[0];

            var operations = Renderer.Install(new McpRenderContext(server, Renderer.Scopes[0], os)).Operations;

            operations.Should().NotBeEmpty("a text with a quote is left out of the command, but the operator keeps the file entry");
            operations.Where(o => o.Kind == McpOperationKinds.Command).Should().OnlyContain(o => !o.Text.Contains(literal, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("&")]
    [InlineData("|")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("^")]
    [InlineData("%X%")]
    [InlineData("!")]
    [InlineData("\"")]
    public void should_leave_out_of_a_windows_command_a_text_that_cmd_exe_reads(string special)
    {
        // PowerShell hands a new command line to a batch shim, and cmd.exe reads these whatever the quotes around them.
        var risky = $"a{special}b";
        var servers = TestCatalog.Parse($$"""
            mcpServers:
              - id: remote
                transport: http
                http: { url: "https://remote.homelab.example/mcp", headers: { X-Team: '{{risky}}' } }
              - id: local
                transport: stdio
                stdio: { command: npx, args: ["-y", "@example/pkg@1.0.0", '--opt={{risky}}'] }
            """).Catalog.McpServerList;

        foreach (var server in servers)
        {
            var operations = Renderer.Install(new McpRenderContext(server, Renderer.Scopes[0], McpOperatingSystems.Windows)).Operations;

            operations.Should().NotBeEmpty("the operator keeps the file entry");
            operations.Where(o => o.Kind == McpOperationKinds.Command).Should().OnlyContain(o => !o.Text.Contains(risky, StringComparison.Ordinal), $"{server.Id}: cmd.exe would read '{special}'");
        }
    }
}
