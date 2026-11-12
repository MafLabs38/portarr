using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Clients;
using Portarr.Core.Mcp.Secrets;
using Portarr.Test.Common;
using Portarr.Test.Common.Mcp;

namespace Portarr.Core.Test.Mcp;

/// <summary>
/// What the Core composes around an adapter (D-113): which client, which scope and system with their defaults, the variables and the provisioning,
/// the notes that hold for every client. Tested with stand-ins: no real adapter is needed to know the Core does its part.
/// </summary>
public sealed class McpRenderingServiceFixture
{
    private const string Store = "secretStore: { kind: vault, address: \"https://vault.homelab.example\", prefix: kv/mcp }\n";

    private const string Plain = """
        mcpServers:
          - id: memory
            transport: http
            http: { url: "https://memory.homelab.example/mcp", headers: { X-Api-Key: { env: MEMORY_MCP_KEY } } }
          - id: github
            transport: stdio
            scope: project
            stdio: { command: npx, args: ["-y", "pkg@1.0.0"], env: { GITHUB_TOKEN: { env: GITHUB_TOKEN } } }
        """;

    private const string WithSecret = """
        mcpServers:
          - id: memory
            transport: http
            http: { url: "https://memory.homelab.example/mcp", headers: { X-Api-Key: { env: MEMORY_MCP_KEY, secret: memory } } }
          - id: github
            transport: stdio
            scope: project
            stdio: { command: npx, args: ["-y", "pkg@1.0.0"], env: { GITHUB_TOKEN: { env: GITHUB_TOKEN } } }
        """;

    private const string Servers = Store + WithSecret;

    private static McpRenderingService Service(string yaml, IMcpClientRegistry? clients = null)
    {
        var catalog = TestCatalog.Parse(yaml).Catalog;
        var catalogService = Substitute.For<ICatalogService>();
        catalogService.Current.Returns(new CatalogState(catalog, "catalog.yaml", true, [], new FakeTimeProvider().GetUtcNow()));
        return new McpRenderingService(clients ?? TestMcp.Clients, TestMcp.Stores, catalogService);
    }

    private static McpServerDefinition Server(string yaml, string id) => TestCatalog.Parse(yaml).Catalog.McpServerList.Single(s => s.Id == id);

    [Fact]
    public void should_default_the_scope_to_the_servers_own_and_the_system_to_linux()
    {
        var rendering = Service(Servers).Install(Server(Servers, "github"), "cursor");

        rendering.Scope.Should().Be("project");
        rendering.Operations.Should().ContainSingle().Which.Text.Should().Be("cursor add github --scope project (linux)");
        rendering.Supported.Should().BeTrue();
    }

    [Theory]
    [InlineData("nope", null, null, "Unknown client 'nope'. Clients: claude-code, claude-desktop")]
    [InlineData("cursor", "machine", null, "Unknown scope 'machine'. Scopes: user, project, local.")]
    [InlineData("cursor", null, "amiga", "Unknown operating system 'amiga'. Systems: windows, linux, macos.")]
    public void should_name_the_valid_values_when_a_client_a_scope_or_a_system_is_unknown(string client, string? scope, string? os, string expected)
    {
        var act = () => Service(Servers).Install(Server(Servers, "memory"), client, scope, os);

        act.Should().Throw<ArgumentException>().Which.Message.Should().StartWith(expected);
    }

    [Fact]
    public void should_say_a_client_does_not_hold_a_scope_without_asking_the_adapter()
    {
        var rendering = Service(Servers).Install(Server(Servers, "github"), "zed", McpScopes.Project);

        rendering.Supported.Should().BeFalse();
        rendering.Operations.Should().BeEmpty();
        rendering.Notes.Should().ContainSingle().Which.Should().Be("Client zed has no 'project' scope; it holds servers at: user.");
    }

    [Fact]
    public void should_fall_back_to_the_clients_first_scope_when_none_is_asked_for_an_installation_and_a_removal_alike()
    {
        var github = Server(Servers, "github");

        var install = Service(Servers).Install(github, "zed");
        var removal = Service(Servers).Remove(github, "zed");

        install.Scope.Should().Be("user");
        install.Supported.Should().BeTrue();
        removal.Scope.Should().Be("user");
        removal.Supported.Should().BeTrue();
    }

    [Fact]
    public void should_list_the_variables_and_provision_the_secrets_from_the_catalogs_store()
    {
        var rendering = Service(Servers).Install(Server(Servers, "memory"), "cursor");

        rendering.RequiresEnv.Should().Equal("MEMORY_MCP_KEY");
        rendering.RequiresSecrets.Should().Equal("kv/mcp/memory");
        rendering.Operations.Last().Should().Be(new McpOperation(McpOperationKinds.Provision, "Stand-in provisioning.", "MEMORY_MCP_KEY <- kv/mcp/memory"));
        rendering.Notes.Should().Equal("Note of cursor.", "Note of the vault store.");
    }

    [Fact]
    public void should_give_no_provisioning_to_a_client_that_cannot_read_a_variable_and_say_what_to_do()
    {
        var clients = new McpClientRegistry([new StandInMcpClient("plain", readsVariables: false, McpScopes.User)]);

        var rendering = Service(Servers, clients).Install(Server(Servers, "memory"), "plain");

        rendering.Operations.Should().NotContain(o => o.Kind == McpOperationKinds.Provision);
        rendering.RequiresSecrets.Should().Equal("kv/mcp/memory");
        rendering.Notes.Should().Contain(n => n.Contains("cannot read a variable", StringComparison.Ordinal) && n.Contains("keep it private", StringComparison.Ordinal));
        rendering.Notes.Should().NotContain("Note of the vault store.");
    }

    [Fact]
    public void should_provision_nothing_without_a_store_or_a_secret_or_for_a_removal()
    {
        // the catalog the service reads has no store: the server (read from a catalog that has one) asks for a secret, and nothing is provisioned
        Service(Plain).Install(Server(Servers, "memory"), "cursor").Operations.Should().NotContain(o => o.Kind == McpOperationKinds.Provision);
        Service(Servers).Install(Server(Servers, "github"), "cursor").Operations.Should().NotContain(o => o.Kind == McpOperationKinds.Provision);
        var removal = Service(Servers).Remove(Server(Servers, "memory"), "cursor");
        removal.Operations.Should().ContainSingle().Which.Kind.Should().Be(McpOperationKinds.Remove);
        removal.RequiresEnv.Should().BeEmpty();
        removal.RequiresSecrets.Should().BeEmpty();
    }

    [Fact]
    public void should_warn_a_windows_operator_about_npx_and_every_operator_about_an_unpinned_package()
    {
        var windows = Service(Servers).Install(Server(Servers, "github"), "cursor", null, McpOperatingSystems.Windows);
        var linux = Service(Servers).Install(Server(Servers, "github"), "cursor", null, McpOperatingSystems.Linux);

        windows.Notes.Should().Contain(n => n.Contains("/c npx", StringComparison.Ordinal));
        linux.Notes.Should().NotContain(n => n.Contains("npx", StringComparison.Ordinal));
        windows.Notes.Should().NotContain(n => n.Contains("PowerShell", StringComparison.Ordinal), "how a command is quoted is the adapter's to say");

        const string unpinned = "mcpServers:\n  - { id: loose, transport: stdio, stdio: { command: npx, args: [\"-y\", \"@example/pkg\"] } }";
        var rendering = Service(unpinned).Install(Server(unpinned, "loose"), "cursor");
        rendering.Notes.Should().Contain(n => n.Contains("without a pinned version", StringComparison.Ordinal));
        Service(unpinned).Remove(Server(unpinned, "loose"), "cursor").Notes.Should().NotContain(n => n.Contains("pinned", StringComparison.Ordinal));
    }

    [Fact]
    public void should_render_in_every_client_each_at_the_scope_it_holds()
    {
        var all = Service(Servers).InstallEverywhere(Server(Servers, "github"));

        all.Select(r => r.Client).Should().Equal(TestMcp.ClientIds);
        all.Single(r => r.Client == "zed").Scope.Should().Be("user");
        all.Single(r => r.Client == "cursor").Scope.Should().Be("project");
        all.Should().OnlyContain(r => r.Supported);
    }

    [Theory]
    [InlineData(null, "linux")]
    [InlineData("  ", "linux")]
    [InlineData(" Windows ", "windows")]
    public void should_normalise_the_system(string? os, string expected)
    {
        Service(Servers).NormalizeOs(os).Should().Be(expected);
    }

    /// <summary>
    /// The acceptance of D-113: a client from another library is one class and its registration. Nothing of the Core changes: the catalog accepts
    /// it in a workstation, the service renders it, and the other clients are untouched.
    /// </summary>
    [Fact]
    public void should_take_a_client_that_registered_without_any_change_to_the_core()
    {
        var clients = new McpClientRegistry([.. TestMcp.Clients.All, new StandInMcpClient("continue", McpScopes.User)]);
        var vocabulary = new McpVocabulary(clients, TestMcp.Stores);

        var parsed = CatalogParser.Parse(Servers + "\nworkstations:\n  - { id: pc, os: linux, clients: [continue, cursor], mcp: [memory] }", TestCatalog.Connectors, null, vocabulary);
        var rendering = Service(Servers, clients).Install(parsed.Catalog.McpServerList[0], "continue", null, null);

        parsed.Errors.Should().BeEmpty();
        parsed.Catalog.WorkstationList.Single().Clients.Should().Equal("continue", "cursor");
        rendering.Operations.First().Text.Should().Be("continue add memory --scope user (linux)");
        clients.Ids.Should().HaveCount(10);
    }

    [Fact]
    public void should_warn_that_an_argument_with_a_variable_reference_is_expanded_by_the_program_only()
    {
        const string yaml = "mcpServers:\n  - { id: bridge, transport: stdio, stdio: { command: npx, args: [\"-y\", \"mcp-remote@1.0.0\", \"https://x.homelab.example\", \"--header\", \"Authorization:${AUTH}\"], env: { AUTH: { env: AUTH_VALUE } } } }";

        var rendering = Service(yaml).Install(Server(yaml, "bridge"), "cursor");

        rendering.Notes.Should().Contain(n => n.Contains("the program does", StringComparison.Ordinal));
        Service(Servers).Install(Server(Servers, "github"), "cursor").Notes.Should().NotContain(n => n.Contains("the program does", StringComparison.Ordinal));
    }
}
