using System.Runtime.CompilerServices;
using System.Text;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp.Clients;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Mcp;

/// <summary>
/// Golden files for the renderers (D-113): <c>Golden/&lt;client&gt;.txt</c> holds, for one remote and one local server, what
/// the client gets at each of its scopes, on Linux, plus the Windows paths, the commands and a removal. A client whose format changes
/// shows as a diff of its file. To refresh them after a deliberate change: <c>PORTARR_UPDATE_GOLDEN=1</c>, then read the diff.
/// </summary>
public sealed class McpClientRendererFixture
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

    public static TheoryData<string> Clients() => new(McpClients.Ids);

    private static IReadOnlyList<McpServerDefinition> Servers => TestCatalog.Parse(Catalog).Catalog.McpServerList;

    [Theory]
    [MemberData(nameof(Clients))]
    public void should_render_what_the_golden_file_holds(string client)
    {
        var renderer = McpClients.Find(client)!;
        var text = new StringBuilder();
        foreach (var scope in renderer.Scopes)
        {
            foreach (var server in Servers)
            {
                Case(text, renderer.Install(server, scope, McpOperatingSystems.Linux), "install", McpOperatingSystems.Linux);
            }
        }

        foreach (var server in Servers)
        {
            Case(text, renderer.Install(server, renderer.Scopes[0], McpOperatingSystems.Windows), "install", McpOperatingSystems.Windows);
            Case(text, renderer.Install(server, renderer.Scopes[0], McpOperatingSystems.MacOs), "install", McpOperatingSystems.MacOs);
        }

        Case(text, renderer.Remove(Servers[0], renderer.Scopes[0], McpOperatingSystems.Linux), "remove", McpOperatingSystems.Linux);
        Case(text, renderer.Remove(Servers[1], renderer.Scopes[0], McpOperatingSystems.Linux), "remove", McpOperatingSystems.Linux);

        Golden(client, text.ToString());
    }

    [Fact]
    public void should_know_nine_clients_with_distinct_ids()
    {
        McpClients.Ids.Should().BeEquivalentTo("claude-code", "claude-desktop", "cursor", "vscode", "windsurf", "gemini-cli", "codex-cli", "opencode", "zed");
        McpClients.Ids.Distinct().Should().HaveSameCount(McpClients.Ids);
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void should_render_every_server_at_every_scope_and_system(string client)
    {
        var renderer = McpClients.Find(client)!;
        foreach (var server in Servers)
        {
            foreach (var scope in McpScopes.All)
            {
                foreach (var os in McpOperatingSystems.All)
                {
                    var rendering = renderer.Install(server, scope, os);

                    rendering.Supported.Should().Be(renderer.Scopes.Contains(scope));
                    if (rendering.Supported)
                    {
                        rendering.Operations.Should().NotBeEmpty().And.OnlyContain(op => op.Text.Length > 0);
                    }
                    else
                    {
                        rendering.Operations.Should().BeEmpty();
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void should_say_which_variables_the_workstation_must_provide(string client)
    {
        var renderer = McpClients.Find(client)!;

        renderer.Install(Servers[0], renderer.Scopes[0], McpOperatingSystems.Linux).RequiresEnv.Should().Equal("MEMORY_MCP_KEY");
        renderer.Install(Servers[1], renderer.Scopes[0], McpOperatingSystems.Linux).RequiresEnv.Should().Equal("GITHUB_TOKEN");
        renderer.Remove(Servers[0], renderer.Scopes[0], McpOperatingSystems.Linux).RequiresEnv.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void should_remove_only_if_present_and_touch_nothing_else(string client)
    {
        var renderer = McpClients.Find(client)!;

        var rendering = renderer.Remove(Servers[0], renderer.Scopes[0], McpOperatingSystems.Linux);

        rendering.Operations.Should().NotBeEmpty().And.OnlyContain(op => op.Kind == McpOperationKinds.Remove && op.Description.Contains("If present", StringComparison.Ordinal));
    }

    [Fact]
    public void should_refuse_a_scope_the_client_does_not_have()
    {
        var rendering = McpClients.Find("zed")!.Install(Servers[0], McpScopes.Project, McpOperatingSystems.Linux);

        rendering.Supported.Should().BeFalse();
        rendering.Operations.Should().BeEmpty();
        rendering.Notes.Should().ContainSingle().Which.Should().Contain("no 'project' scope");
    }

    [Fact]
    public void should_leave_the_command_out_when_a_value_holds_a_quote_and_keep_the_file()
    {
        var server = TestCatalog.Parse("""
            mcpServers:
              - id: odd
                transport: http
                http: { url: "https://odd.homelab.example/mcp", headers: { X-Team: "O'Brien" } }
            """).Catalog.McpServerList.Single();

        var rendering = McpClients.Find("claude-code")!.Install(server, McpScopes.User, McpOperatingSystems.Linux);

        rendering.Operations.Should().ContainSingle().Which.Kind.Should().Be(McpOperationKinds.File);
    }

    [Fact]
    public void should_advise_on_windows_when_the_command_is_npx()
    {
        var renderer = McpClients.Find("cursor")!;

        renderer.Install(Servers[1], McpScopes.User, McpOperatingSystems.Windows).Notes.Should().Contain(note => note.Contains("cmd", StringComparison.Ordinal));
        renderer.Install(Servers[1], McpScopes.User, McpOperatingSystems.Linux).Notes.Should().BeEmpty();
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
        }

        File.Exists(path).Should().BeTrue($"the golden file {client}.txt must exist: run once with PORTARR_UPDATE_GOLDEN=1 and read it");
        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(actual.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("it's")]
    [InlineData("it\u2018s")]
    [InlineData("it\u2019s")]
    [InlineData("it\u201As")]
    [InlineData("it\u201Bs")]
    public void should_never_put_a_quote_character_inside_the_quotes_of_a_command(string literal)
    {
        var server = TestCatalog.Parse($$"""
            mcpServers:
              - id: tricky
                transport: stdio
                stdio:
                  command: npx
                  args: ["-y", "{{literal}}"]
            """).Catalog.McpServerList[0];

        foreach (var client in new[] { "claude-code", "gemini-cli", "codex-cli" })
        {
            var rendering = McpClients.Find(client)!.Install(server, McpClients.Find(client)!.Scopes[0], McpOperatingSystems.Windows);

            rendering.Operations.Where(o => o.Kind == McpOperationKinds.Command).Should().OnlyContain(o => !o.Text.Contains(literal, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void should_tell_the_operator_to_use_powershell_for_the_commands_of_a_windows_machine()
    {
        var rendering = McpClients.Find("claude-code")!.Install(Servers[0], "user", McpOperatingSystems.Windows);

        rendering.Notes.Should().Contain(n => n.Contains("PowerShell", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void should_name_the_variable_and_never_carry_its_value(string client)
    {
        const string name = "PORTARR_TEST_MARKER_KEY";
        const string value = "marker-value-that-must-never-be-rendered";
        var server = TestCatalog.Parse($$"""
            mcpServers:
              - id: remote
                transport: http
                http: { url: "https://remote.homelab.example/mcp", headers: { X-Api-Key: { env: {{name}} } } }
              - id: local
                transport: stdio
                stdio: { command: npx, args: ["-y", "@example/local-mcp@1.0.0"], env: { LOCAL_KEY: { env: {{name}} } } }
            """).Catalog.McpServerList;
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            var renderer = McpClients.Find(client)!;
            var text = string.Join('\n', server.SelectMany(s => renderer.Install(s, renderer.Scopes[0], McpOperatingSystems.Linux).Operations).Select(o => o.Text));

            text.Should().Contain(name).And.NotContain(value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
