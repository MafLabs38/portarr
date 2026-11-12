using System.Runtime.CompilerServices;
using System.Text;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Clients;
using CatalogModel = Portarr.Core.Catalog.Catalog;

namespace Portarr.Providers.McpClients.Test;

/// <summary>
/// Golden files for the renderers (D-113): <c>Golden/&lt;client&gt;.txt</c> holds, for one remote and one local server, what the client gets at
/// each of its scopes, on Linux, plus the Windows paths, the commands and a removal, as the Core composes it from the adapter. A client whose
/// format changes shows as a diff of its file. To refresh them after a deliberate change: <c>PORTARR_UPDATE_GOLDEN=1</c>, then read the diff.
/// </summary>
public sealed class GoldenRenderingFixture
{
    private const string Catalog = """
        mcpServers:
          - id: memory
            name: Memory
            transport: http
            http:
              url: https://memory.homelab.example/mcp
              headers:
                X-Api-Key: { env: MEMORY_MCP_KEY }
                Accept: application/json
          - id: github
            name: GitHub
            transport: stdio
            stdio:
              command: npx
              args: ["-y", "@example/github-mcp@1.4.2"]
              env: { GITHUB_TOKEN: { env: GITHUB_TOKEN }, LOG_LEVEL: info }
        """;

    public static TheoryData<string> Clients() => new(McpClientsProvider.Renderers().Select(r => r.Id));

    private static CatalogModel Parsed => RealMcp.Parse(Catalog).Catalog;

    private static IReadOnlyList<McpServerDefinition> Servers => Parsed.McpServerList;

    [Theory]
    [MemberData(nameof(Clients))]
    public void should_render_what_the_golden_file_holds(string client)
    {
        var rendering = RealMcp.Rendering(Parsed);
        var scopes = RealMcp.Clients.Find(client)!.Scopes;
        var text = new StringBuilder();
        foreach (var scope in scopes)
        {
            foreach (var server in Servers)
            {
                Case(text, rendering.Install(server, client, scope, McpOperatingSystems.Linux), "install", McpOperatingSystems.Linux);
            }
        }

        foreach (var server in Servers)
        {
            Case(text, rendering.Install(server, client, scopes[0], McpOperatingSystems.Windows), "install", McpOperatingSystems.Windows);
            Case(text, rendering.Install(server, client, scopes[0], McpOperatingSystems.MacOs), "install", McpOperatingSystems.MacOs);
        }

        Case(text, rendering.Remove(Servers[0], client, scopes[0], McpOperatingSystems.Linux), "remove", McpOperatingSystems.Linux);
        Case(text, rendering.Remove(Servers[1], client, scopes[0], McpOperatingSystems.Linux), "remove", McpOperatingSystems.Linux);

        Golden(client, text.ToString());
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void should_remove_only_if_present_and_touch_nothing_else(string client)
    {
        var rendering = RealMcp.Rendering(Parsed).Remove(Servers[0], client, null, McpOperatingSystems.Linux);

        rendering.Operations.Should().NotBeEmpty().And.OnlyContain(op => op.Kind == McpOperationKinds.Remove && op.Description.Contains("If present", StringComparison.Ordinal));
    }

    [Fact]
    public void should_leave_the_command_out_when_a_value_holds_a_quote_and_keep_the_file()
    {
        var catalog = RealMcp.Parse("""
            mcpServers:
              - id: odd
                transport: http
                http: { url: "https://odd.homelab.example/mcp", headers: { X-Team: "O'Brien" } }
            """).Catalog;

        var rendering = RealMcp.Rendering(catalog).Install(catalog.McpServerList.Single(), "claude-code", McpScopes.User, McpOperatingSystems.Linux);

        rendering.Operations.Should().ContainSingle().Which.Kind.Should().Be(McpOperationKinds.File);
    }

    private static void Case(StringBuilder text, McpRendering rendering, string action, string os)
    {
        text.Append("### ").Append(action).Append(' ').Append(rendering.ServerId).Append(" | ").Append(rendering.Scope).Append(" | ").AppendLine(os);
        text.Append("supported: ").AppendLine(rendering.Supported ? "yes" : "no");
        if (rendering.RequiresEnv.Count > 0)
        {
            text.Append("requires: ").AppendLine(string.Join(", ", rendering.RequiresEnv));
        }

        foreach (var operation in rendering.Operations)
        {
            text.Append('[').Append(operation.Kind).Append(']');
            if (operation.Path is not null)
            {
                text.Append(' ').Append(operation.Path);
            }

            text.AppendLine();
            text.Append("  ").AppendLine(operation.Description);
            foreach (var line in operation.Text.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                text.AppendLine(line.Length == 0 ? string.Empty : "    " + line);
            }
        }

        foreach (var note in rendering.Notes)
        {
            text.Append("note: ").AppendLine(note);
        }

        text.AppendLine();
    }

    private static void Golden(string client, string actual, [CallerFilePath] string source = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(source)!, "Golden", client + ".txt");
        if (Environment.GetEnvironmentVariable("PORTARR_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
            Assert.Fail($"The golden file {client}.txt was rewritten: read the diff, then run again without PORTARR_UPDATE_GOLDEN.");
        }

        File.Exists(path).Should().BeTrue($"the golden file {client}.txt must exist: run once with PORTARR_UPDATE_GOLDEN=1 and read it");
        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(actual.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void should_quote_an_argument_with_a_comma_which_powershell_would_split_in_two()
    {
        var catalog = RealMcp.Parse("""
            mcpServers:
              - id: fs
                transport: stdio
                stdio: { command: npx, args: ["-y", "@example/fs-mcp@1.0.0", "--allow=/srv/a,/srv/b"] }
            """).Catalog;

        var rendering = RealMcp.Rendering(catalog).Install(catalog.McpServerList.Single(), "claude-code", McpScopes.User, McpOperatingSystems.Windows);

        rendering.Operations.First(o => o.Kind == McpOperationKinds.Command).Text.Should().Contain("'--allow=/srv/a,/srv/b'");
    }
}
