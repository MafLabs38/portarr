using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Portarr.Integration.Test.Api;

/// <summary>Wiring, JSON shape and permission of the MCP endpoints (V9). The rendering rules themselves are unit-tested (McpClientRendererFixture).</summary>
[Trait("Category", "Integration")]
public sealed class McpServersFixture : IAsyncLifetime
{
    // check: false keeps the host from probing a name that does not exist: the health has its own tests.
    private const string Catalog = """
        mcpServers:
          - id: memory
            name: Memory
            transport: http
            http:
              url: https://memory.homelab.example/mcp
              headers: { X-Api-Key: { env: MEMORY_MCP_KEY } }
            check: false
          - id: github
            name: GitHub
            transport: stdio
            scope: project
            stdio: { command: npx, args: ["-y", "@example/github-mcp"], env: { GITHUB_TOKEN: { env: GITHUB_TOKEN } } }
        workstations:
          - id: dev-laptop
            name: Dev laptop
            os: windows
            clients: [claude-code]
            mcp: [memory]
        """;

    private readonly TestDatabase _database;
    private PortarrFactory _factory = null!;

    public McpServersFixture(TestDatabase database)
    {
        _database = database;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), Catalog, Token);
        _factory = new PortarrFactory(options);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task should_list_the_servers_without_any_secret()
    {
        using var client = await _factory.CreateSignedInClientAsync("Admin");

        var body = await client.GetStringAsync("/api/v1/mcp/servers", Token);

        using var json = JsonDocument.Parse(body);
        var servers = json.RootElement.EnumerateArray().ToList();
        servers.Select(s => s.GetProperty("id").GetString()).Should().Equal("memory", "github");
        servers[0].GetProperty("status").GetString().Should().Be("not-checked");
        servers[0].GetProperty("url").GetString().Should().Be("https://memory.homelab.example/mcp");
        servers[0].GetProperty("values")[0].GetProperty("variable").GetString().Should().Be("MEMORY_MCP_KEY");
        servers[1].GetProperty("command").GetString().Should().Be("npx");
        servers[1].GetProperty("unpinnedPackage").GetBoolean().Should().BeTrue();
        body.Should().NotContain("password").And.NotContain("token\":\"");
    }

    [Fact]
    public async Task should_render_a_server_for_every_client_at_the_operating_system_asked()
    {
        using var client = await _factory.CreateSignedInClientAsync("Admin");

        var body = await client.GetStringAsync("/api/v1/mcp/servers/memory?os=windows", Token);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("os").GetString().Should().Be("windows");
        var renderings = json.RootElement.GetProperty("renderings").EnumerateArray().ToList();
        renderings.Should().HaveCount(9);
        var claude = renderings.Single(r => r.GetProperty("client").GetString() == "claude-code");
        claude.GetProperty("operations").EnumerateArray().Select(o => o.GetProperty("kind").GetString()).Should().Equal("command", "file");
        claude.GetProperty("requiresEnv")[0].GetString().Should().Be("MEMORY_MCP_KEY");
    }

    [Fact]
    public async Task should_answer_404_for_an_unknown_server_and_400_for_an_unknown_system()
    {
        using var client = await _factory.CreateSignedInClientAsync("Admin");

        (await client.GetAsync("/api/v1/mcp/servers/ghost", Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/mcp/servers/memory?os=amiga", Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_list_the_workstations_with_what_is_wanted_on_them()
    {
        using var client = await _factory.CreateSignedInClientAsync("Admin");

        var workstations = await client.GetFromJsonAsync<JsonElement>("/api/v1/mcp/workstations", Token);

        var laptop = workstations.EnumerateArray().Should().ContainSingle().Subject;
        laptop.GetProperty("os").GetString().Should().Be("windows");
        laptop.GetProperty("wanted")[0].GetProperty("server").GetString().Should().Be("memory");
        laptop.GetProperty("wanted")[0].GetProperty("clients")[0].GetString().Should().Be("claude-code");
    }

    [Fact]
    public async Task should_give_the_plan_of_a_workstation_for_its_own_system_unless_another_is_asked()
    {
        using var client = await _factory.CreateSignedInClientAsync("Admin");

        var plan = await client.GetFromJsonAsync<JsonElement>("/api/v1/mcp/workstations/dev-laptop", Token);
        var linux = await client.GetFromJsonAsync<JsonElement>("/api/v1/mcp/workstations/dev-laptop?os=linux", Token);

        plan.GetProperty("os").GetString().Should().Be("windows");
        plan.GetProperty("install")[0].GetProperty("server").GetString().Should().Be("memory");
        plan.GetProperty("install")[0].GetProperty("operations")[1].GetProperty("path").GetString().Should().Contain("USERPROFILE");
        plan.GetProperty("remove").EnumerateArray().Select(r => r.GetProperty("server").GetString()).Should().Equal("github");
        plan.GetProperty("requiresEnv")[0].GetString().Should().Be("MEMORY_MCP_KEY");
        linux.GetProperty("install")[0].GetProperty("operations")[1].GetProperty("path").GetString().Should().Be("~/.claude.json");
    }

    [Fact]
    public async Task should_answer_404_for_an_unknown_workstation_and_400_for_an_unknown_system()
    {
        using var client = await _factory.CreateSignedInClientAsync("Admin");

        (await client.GetAsync("/api/v1/mcp/workstations/nobody", Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/mcp/workstations/dev-laptop?os=amiga", Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_list_the_clients_and_their_scopes()
    {
        using var client = await _factory.CreateSignedInClientAsync("Admin");

        var clients = await client.GetFromJsonAsync<JsonElement>("/api/v1/mcp/clients", Token);

        clients.GetArrayLength().Should().Be(9);
        clients.EnumerateArray().Single(c => c.GetProperty("id").GetString() == "zed").GetProperty("scopes").GetArrayLength().Should().Be(1);
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Operator", HttpStatusCode.OK)]
    [InlineData("Observer", HttpStatusCode.Forbidden)]
    [InlineData("Guest", HttpStatusCode.Forbidden)]
    public async Task should_give_the_mcp_page_to_the_roles_that_configure_the_environment_and_not_to_those_that_look_at_it(string role, HttpStatusCode expected)
    {
        using var client = await _factory.CreateSignedInClientAsync(role);

        (await client.GetAsync("/api/v1/mcp/servers", Token)).StatusCode.Should().Be(expected);
        (await client.GetAsync("/api/v1/mcp/servers/memory", Token)).StatusCode.Should().Be(expected);
        (await client.GetAsync("/api/v1/mcp/clients", Token)).StatusCode.Should().Be(expected);
        (await client.GetAsync("/api/v1/mcp/workstations", Token)).StatusCode.Should().Be(expected);
        (await client.GetAsync("/api/v1/mcp/workstations/dev-laptop", Token)).StatusCode.Should().Be(expected);
    }
}
