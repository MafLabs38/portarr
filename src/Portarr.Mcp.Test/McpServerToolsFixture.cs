using System.Security.Claims;
using ModelContextProtocol;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp;
using Portarr.Http.Authentication;
using Portarr.Mcp.Digest;
using Portarr.Mcp.Server;
using Portarr.Test.Common;
using Portarr.Test.Common.Mcp;

namespace Portarr.Mcp.Test;

/// <summary>What the MCP tools tell an assistant about the operator's MCP servers: their health, and how to install one for its own client.</summary>
public sealed class McpServerToolsFixture
{
    private const string Catalog = """
        mcpServers:
          - id: memory
            name: Memory
            transport: http
            http:
              url: https://memory.homelab.example/mcp
              headers: { X-Api-Key: { env: MEMORY_MCP_KEY } }
            notes: Long-term memory.
          - id: github
            transport: stdio
            scope: project
            stdio: { command: npx, args: ["-y", "@example/github-mcp@1.4.2"] }
        workstations:
          - id: dev-laptop
            name: Dev laptop
            os: windows
            clients: [claude-code, cursor]
            mcp:
              - memory
              - { server: github, clients: [claude-code] }
        """;

    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, McpServerHealth> _health = new()
    {
        ["memory"] = new McpServerHealth("memory", McpHealthState.Down, Now.AddDays(-12), Now.AddDays(-12), Now, "timeout", null),
    };

    private McpServerTools Tools
    {
        get
        {
            var catalog = Substitute.For<ICatalogService>();
            catalog.Current.Returns(new CatalogState(TestCatalog.Parse(Catalog).Catalog, "catalog.yaml", true, [], Now));
            var health = Substitute.For<IMcpHealth>();
            health.Current.Returns(_health);
            var servers = new McpServerService(catalog, health);
            var rendering = TestMcp.Rendering(catalog);
            return new McpServerTools(servers, new McpWorkstationService(catalog, servers, rendering), rendering);
        }
    }

    [Fact]
    public void should_list_the_servers_with_their_health_and_without_a_secret()
    {
        var lines = Tools.ListMcpServers();

        lines.Should().HaveCount(2);
        var memory = lines[0];
        memory.Status.Should().Be("down");
        memory.Since.Should().Be("2026-10-31T09:00:00.0000000+00:00");
        memory.LastHealthyAt.Should().Be("2026-10-31T09:00:00.0000000+00:00");
        memory.Error.Should().Be("timeout");
        memory.Url.Should().Be("https://memory.homelab.example/mcp");
        memory.Notes.Should().Be("Long-term memory.");

        var github = lines[1];
        github.Status.Should().Be("not-checked");
        github.Command.Should().Be("npx -y @example/github-mcp@1.4.2");
        github.Since.Should().BeNull();
    }

    [Fact]
    public void should_say_a_remote_server_is_waiting_until_its_first_check()
    {
        _health.Clear();

        Tools.ListMcpServers()[0].Status.Should().Be("waiting");
    }

    [Fact]
    public void should_render_for_the_client_that_asks_at_the_default_scope_for_linux()
    {
        var answer = Tools.GetMcpServer("memory", "claude-code");

        answer.Client.Should().Be("claude-code");
        answer.Scope.Should().Be("user");
        answer.Os.Should().Be("linux");
        answer.Action.Should().Be("install");
        answer.Supported.Should().BeTrue();
        answer.RequiresEnv.Should().Equal("MEMORY_MCP_KEY");
        answer.Operations.Select(o => o.Kind).Should().Equal("command");
        answer.Instructions.Should().Contain("wait for their approval").And.Contain("never write a secret value");
    }

    [Fact]
    public void should_find_a_server_by_name_and_use_its_own_scope()
    {
        var answer = Tools.GetMcpServer("GitHub", "cursor", os: "windows");

        answer.Scope.Should().Be("project");
        answer.Os.Should().Be("windows");
        answer.Operations.Should().ContainSingle().Which.Text.Should().Contain("(windows)");
    }

    [Fact]
    public void should_describe_a_removal_when_asked()
    {
        var answer = Tools.GetMcpServer("memory", "claude-code", action: "remove");

        answer.Action.Should().Be("remove");
        answer.RequiresEnv.Should().BeEmpty();
        answer.Operations.Should().OnlyContain(o => o.Kind == "remove");
    }

    [Fact]
    public void should_say_when_the_client_cannot_hold_the_server_at_that_scope()
    {
        var answer = Tools.GetMcpServer("memory", "zed", scope: "project");

        answer.Supported.Should().BeFalse();
        answer.Operations.Should().BeEmpty();
        answer.Notes.Should().ContainSingle().Which.Should().Contain("no 'project' scope");
    }

    [Theory]
    [InlineData("ghost", "claude-code", null, null, "No MCP server 'ghost'. Servers: memory, github.")]
    [InlineData("memory", "notepad", null, null, "Unknown client 'notepad'. Clients: claude-code")]
    [InlineData("memory", "cursor", "everywhere", null, "Unknown scope 'everywhere'. Scopes: user, project, local.")]
    [InlineData("memory", "cursor", null, "amiga", "Unknown operating system 'amiga'. Systems: windows, linux, macos.")]
    [InlineData("memory", "cursor", null, null, "Unknown action 'reinstall'. Actions: install, remove.")]
    public void should_name_the_valid_values_when_a_request_is_wrong(string server, string client, string? scope, string? os, string expected)
    {
        var action = expected.Contains("Unknown action", StringComparison.Ordinal) ? "reinstall" : null;

        var act = () => Tools.GetMcpServer(server, client, scope, os, action);

        act.Should().Throw<McpException>().Which.Message.Should().StartWith(expected);
    }

    [Fact]
    public void should_hide_a_dead_mcp_server_from_whoever_may_not_see_the_mcp_servers()
    {
        var overview = new FleetBuilder().Digest.GetOverview() with
        {
            NeedsAttention = [new Finding("mcp", "warning", "mcp-server-down", 1, ["Memory (down for 12 d)"]), new Finding("apps", "danger", "app-down", 1, ["Blog"])],
        };

        var observer = Caller(Permissions.AppsView, Permissions.InfrastructureView);
        var operatorPrincipal = Caller(Permissions.AppsView, Permissions.McpUse);

        PortarrTools.ForCaller(overview, observer).NeedsAttention.Select(f => f.Kind).Should().Equal("app-down");
        PortarrTools.ForCaller(overview, null).NeedsAttention.Select(f => f.Kind).Should().Equal("app-down");
        PortarrTools.ForCaller(overview, operatorPrincipal).NeedsAttention.Select(f => f.Kind).Should().Equal("mcp-server-down", "app-down");
    }

    [Fact]
    public void should_list_the_workstations_with_what_is_wanted_on_them()
    {
        var line = Tools.ListWorkstations().Should().ContainSingle().Subject;

        line.Id.Should().Be("dev-laptop");
        line.Os.Should().Be("windows");
        line.Clients.Should().Equal("claude-code", "cursor");
        line.Wanted.Select(w => $"{w.Server}:{string.Join('+', w.Clients)}").Should().Equal("memory:claude-code+cursor", "github:claude-code");
    }

    [Fact]
    public void should_give_the_plan_of_the_machine_the_assistant_is_on()
    {
        var plan = Tools.GetWorkstationPlan("Dev laptop");

        plan.Workstation.Should().Be("dev-laptop");
        plan.Os.Should().Be("windows");
        plan.Install.Select(i => $"{i.Server}/{i.Client}").Should().Equal("memory/claude-code", "memory/cursor", "github/claude-code");
        plan.Remove.Select(i => $"{i.Server}/{i.Client}").Should().Equal("github/cursor");
        plan.RequiresEnv.Should().Equal("MEMORY_MCP_KEY");
        plan.Instructions.Should().Contain("wait for their approval");
    }

    [Theory]
    [InlineData("nobody", null, "No workstation 'nobody'. Workstations: dev-laptop.")]
    [InlineData("dev-laptop", "amiga", "Unknown operating system 'amiga'.")]
    public void should_name_the_valid_values_when_the_plan_is_asked_wrongly(string workstation, string? os, string expected)
    {
        var act = () => Tools.GetWorkstationPlan(workstation, os);

        act.Should().Throw<McpException>().Which.Message.Should().StartWith(expected);
    }

    private static ClaimsPrincipal Caller(params string[] permissions)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(permissions.Select(p => new Claim(PortarrClaims.Permission, p)), "test"));
    }
}
