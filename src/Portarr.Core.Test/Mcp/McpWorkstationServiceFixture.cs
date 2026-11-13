using Portarr.Core.Catalog;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Clients;
using Portarr.Test.Common;
using Portarr.Test.Common.Mcp;

namespace Portarr.Core.Test.Mcp;

/// <summary>The plan of a workstation (D-110): what to install, what to remove if present, what the machine must provide.</summary>
public sealed class McpWorkstationServiceFixture
{
    private const string Catalog = """
        mcpServers:
          - id: memory
            transport: http
            http: { url: "https://memory.homelab.example/mcp", headers: { X-Api-Key: { env: MEMORY_MCP_KEY } } }
          - id: github
            transport: stdio
            scope: project
            stdio: { command: npx, args: ["-y", "@example/github-mcp@1.4.2"], env: { GITHUB_TOKEN: { env: GITHUB_TOKEN } } }
          - id: search
            transport: http
            http: { url: "https://search.homelab.example/mcp" }
        workstations:
          - id: dev-laptop
            name: Dev laptop
            os: windows
            clients: [claude-code, cursor]
            mcp:
              - memory
              - { server: github, clients: [claude-code] }
          - id: gaming-pc
            os: linux
            clients: [zed]
            mcp: [search]
        """;

    private readonly McpWorkstationService _service;

    public McpWorkstationServiceFixture()
    {
        var catalog = Substitute.For<ICatalogService>();
        catalog.Current.Returns(new CatalogState(TestCatalog.Parse(Catalog).Catalog, "catalog.yaml", true, [], null));
        var health = Substitute.For<IMcpHealth>();
        health.Current.Returns(new Dictionary<string, McpServerHealth>());
        _service = new McpWorkstationService(catalog, new McpServerService(catalog, health), TestMcp.Rendering(catalog));
    }

    [Fact]
    public void should_list_the_workstations_with_the_servers_wanted_on_each()
    {
        var views = _service.List();

        views.Select(v => v.Workstation.Id).Should().Equal("dev-laptop", "gaming-pc");
        views[0].Wanted.Select(s => s.Server.Id).Should().Equal("memory", "github");
        views[1].Wanted.Select(s => s.Server.Id).Should().Equal("search");
    }

    [Fact]
    public void should_find_a_workstation_by_id_or_name()
    {
        _service.Find("dev-laptop")!.Workstation.Id.Should().Be("dev-laptop");
        _service.Find("DEV LAPTOP")!.Workstation.Id.Should().Be("dev-laptop");
        _service.Find("nobody").Should().BeNull();
    }

    [Fact]
    public void should_install_each_wanted_server_in_each_of_its_clients_for_the_system_of_the_machine()
    {
        var plan = _service.GetPlan(_service.Find("dev-laptop")!.Workstation);

        plan.Os.Should().Be("windows");
        plan.Install.Select(r => $"{r.ServerId}/{r.Client}/{r.Scope}").Should().Equal(
            "memory/claude-code/user",
            "memory/cursor/user",
            "github/claude-code/project");
        plan.Install.Single(r => r.Client == "cursor").Operations.Single().Text.Should().Contain("(windows)");
        plan.RequiresEnv.Should().Equal("GITHUB_TOKEN", "MEMORY_MCP_KEY");
    }

    [Fact]
    public void should_remove_if_present_what_is_in_the_catalog_but_not_wanted_in_that_client()
    {
        var plan = _service.GetPlan(_service.Find("dev-laptop")!.Workstation);

        // github is wanted in claude-code only, search nowhere.
        plan.Remove.Select(r => $"{r.ServerId}/{r.Client}").Should().BeEquivalentTo(
            "github/cursor",
            "search/claude-code",
            "search/cursor");
        plan.Remove.SelectMany(r => r.Operations).Should().OnlyContain(o => o.Kind == McpOperationKinds.Remove && o.Description.Contains("If present", StringComparison.Ordinal));
    }

    [Fact]
    public void should_remove_in_the_scope_the_client_holds_when_it_cannot_hold_the_servers_own()
    {
        // github's own scope is project: Zed has only the user scope, where the server page installs it, so that is where a removal looks.
        var plan = _service.GetPlan(_service.Find("gaming-pc")!.Workstation);

        plan.Remove.Select(r => $"{r.ServerId}/{r.Client}/{r.Scope}").Should().BeEquivalentTo("memory/zed/user", "github/zed/user");
        plan.Install.Should().ContainSingle().Which.ServerId.Should().Be("search");
    }

    [Fact]
    public void should_render_for_another_system_when_asked_and_refuse_an_unknown_one()
    {
        var workstation = _service.Find("dev-laptop")!.Workstation;

        _service.GetPlan(workstation, "linux").Install[0].Operations.Single().Text.Should().Contain("(linux)");

        var act = () => _service.GetPlan(workstation, "amiga");
        act.Should().Throw<ArgumentException>().WithMessage("Unknown operating system 'amiga'*");
    }

    [Fact]
    public void should_refuse_an_unknown_system_even_for_a_workstation_that_wants_nothing()
    {
        var act = () => _service.GetPlan(_service.Find("gaming-pc")!.Workstation, "beos");

        act.Should().Throw<ArgumentException>().WithMessage("Unknown operating system 'beos'*");
    }
}
