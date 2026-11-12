using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Mcp.Capture;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// The inbox of captured MCP servers, end to end (D-112, D-116): an assistant hands over what a workstation has, the operator sees it
/// against the catalog, and only a person (a browser session that holds <c>catalog.write</c>) accepts it, through the editor.
/// </summary>
[Trait("Category", "Integration")]
public sealed class McpCapturesFixture
{
    private const string Catalog = """
        mcpServers:
          - id: memory
            name: Memory
            transport: http
            http: { url: "https://memory.homelab.example/mcp", headers: { X-Api-Key: { env: MEMORY_MCP_KEY } } }
            notes: Long-term memory.
            check: false
        workstations:
          - id: dev-laptop
            os: windows
            clients: [claude-code]
            mcp: [memory]
        """;

    private readonly TestDatabase _database;

    public McpCapturesFixture(TestDatabase database)
    {
        _database = database;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CapturedServer Remote(string name, string url, params CapturedValue[] headers) => new(name, "http", url, null, [], headers, []);

    private async Task<(PortarrFactory Factory, string CatalogFile)> StartAsync(bool writable = true)
    {
        var options = await _database.CreateOptionsAsync();
        var file = Path.Combine(options.DataFolder, "catalog.yaml");
        await File.WriteAllTextAsync(file, Catalog, Token);
        return (new PortarrFactory(options, new Dictionary<string, string?> { ["Catalog:Writable"] = writable ? "true" : "false" }), file);
    }

    private static Task<CaptureReceipt> Hand(PortarrFactory factory, string workstation, string client, params CapturedServer[] servers)
    {
        return factory.Services.GetRequiredService<IMcpCaptureService>().SubmitAsync(new CaptureRequest(workstation, client, "user", servers), "assistant", Token);
    }

    private static async Task<List<JsonElement>> GroupsAsync(HttpClient client)
    {
        return (await client.GetFromJsonAsync<JsonElement>("/api/v1/mcp/captures", Token)).EnumerateArray().ToList();
    }

    private static string Key(JsonElement group) => group.GetProperty("key").GetString()!;

    private static string Version(JsonElement group) => group.GetProperty("version").GetString()!;

    [Fact]
    public async Task should_refuse_to_accept_a_server_that_was_handed_over_again_since_the_person_read_it()
    {
        var (factory, file) = await StartAsync();
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");
        var before = await File.ReadAllTextAsync(file, Token);
        await Hand(factory, "dev-laptop", "claude-code", Remote("search", "https://search.homelab.example/mcp"));
        var reviewed = (await GroupsAsync(admin)).Should().ContainSingle().Subject;

        await Hand(factory, "dev-laptop", "claude-code", Remote("search", "https://search.homelab.example/mcp", new CapturedValue("X-Other", null, "changed")));
        var response = await admin.PostAsJsonAsync($"/api/v1/mcp/captures/{Key(reviewed)}/accept", new { version = Version(reviewed), mode = "new", addToWorkstations = false }, Token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await File.ReadAllTextAsync(file, Token)).Should().Be(before);
        var again = (await GroupsAsync(admin)).Should().ContainSingle().Subject;
        Version(again).Should().NotBe(Version(reviewed));
    }

    [Fact]
    public async Task should_keep_a_capture_that_still_waits_however_many_come_after_it()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");
        await Hand(factory, "dev-laptop", "claude-code", Remote("first", "https://first.homelab.example/mcp"));

        for (var i = 0; i < 205; i++)
        {
            await Hand(factory, $"machine-{i}", "claude-code", Remote($"later-{i}", $"https://later-{i}.homelab.example/mcp"));
        }

        var names = (await GroupsAsync(admin)).Select(g => g.GetProperty("server").GetProperty("name").GetString()).ToList();
        names.Should().Contain("first").And.HaveCount(206);
    }

    [Fact]
    public async Task should_show_a_new_server_with_a_proposal_and_add_it_to_the_catalog_and_the_workstation_when_a_person_accepts()
    {
        var (factory, file) = await StartAsync();
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");
        await Hand(factory, "dev-laptop", "claude-code", Remote("Search Tool", "https://search.homelab.example/mcp", new CapturedValue("X-Api-Key", null, null)));

        var group = (await GroupsAsync(admin)).Should().ContainSingle().Subject;

        group.GetProperty("state").GetString().Should().Be("new");
        group.GetProperty("proposal").GetProperty("id").GetString().Should().Be("search-tool");
        group.GetProperty("proposal").GetProperty("variables")[0].GetProperty("variable").GetString().Should().Be("SEARCH_TOOL_X_API_KEY");
        group.GetProperty("sightings")[0].GetProperty("workstationKnown").GetBoolean().Should().BeTrue();

        var accepted = await admin.PostAsJsonAsync($"/api/v1/mcp/captures/{Key(group)}/accept", new { version = Version(group), mode = "new", id = "search", name = "Search", variables = new Dictionary<string, string> { ["header:X-Api-Key"] = "SEARCH_KEY" }, addToWorkstations = true }, Token);

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await accepted.Content.ReadFromJsonAsync<JsonElement>(Token);
        result.GetProperty("written").GetBoolean().Should().BeTrue();
        result.GetProperty("serverId").GetString().Should().Be("search");
        result.GetProperty("workstationsUpdated")[0].GetString().Should().Be("dev-laptop");

        var text = await File.ReadAllTextAsync(file, Token);
        text.Should().Contain("id: search").And.Contain("SEARCH_KEY").And.Contain("Long-term memory.");
        text.Should().Contain("mcp: [memory, search]");

        (await GroupsAsync(admin)).Should().BeEmpty();
        var servers = await admin.GetFromJsonAsync<JsonElement>("/api/v1/mcp/servers", Token);
        servers.EnumerateArray().Select(s => s.GetProperty("id").GetString()).Should().Equal("memory", "search");
    }

    [Fact]
    public async Task should_recognise_a_server_the_catalog_has_whatever_it_is_called_and_only_add_it_to_the_wanted_list()
    {
        var (factory, file) = await StartAsync();
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");
        await Hand(factory, "dev-laptop", "cursor", Remote("Mnemarr-memory", "https://MEMORY.homelab.example/mcp/", new CapturedValue("X-Api-Key", "MEMORY_MCP_KEY", null)));

        var group = (await GroupsAsync(admin)).Should().ContainSingle().Subject;
        group.GetProperty("state").GetString().Should().Be("same");
        group.GetProperty("matchId").GetString().Should().Be("memory");

        var accepted = await admin.PostAsJsonAsync($"/api/v1/mcp/captures/{Key(group)}/accept", new { version = Version(group), mode = "existing", addToWorkstations = true }, Token);

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await File.ReadAllTextAsync(file, Token);
        // The server was wanted in every client of the machine; it is now seen in Cursor too, so the machine has two clients and the bare id still says "all".
        text.Should().Contain("clients: [claude-code, cursor]").And.Contain("mcp: [memory]");
        text.Should().NotContain("Mnemarr-memory");
    }

    [Fact]
    public async Task should_say_what_differs_and_replace_the_definition_keeping_its_notes()
    {
        var (factory, file) = await StartAsync();
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");
        await Hand(factory, "dev-laptop", "claude-code", Remote("memory", "https://memory.homelab.example/mcp", new CapturedValue("X-Api-Key", "OTHER_KEY", null)));

        var group = (await GroupsAsync(admin)).Should().ContainSingle().Subject;
        group.GetProperty("state").GetString().Should().Be("differs");
        group.GetProperty("differences")[0].GetString().Should().Be("header X-Api-Key: variable MEMORY_MCP_KEY → OTHER_KEY");

        var accepted = await admin.PostAsJsonAsync($"/api/v1/mcp/captures/{Key(group)}/accept", new { version = Version(group), mode = "replace", addToWorkstations = false }, Token);

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await File.ReadAllTextAsync(file, Token);
        text.Should().Contain("OTHER_KEY").And.NotContain("MEMORY_MCP_KEY").And.Contain("Long-term memory.");
    }

    [Fact]
    public async Task should_dismiss_without_touching_the_catalog_and_replace_a_waiting_capture_by_a_newer_one()
    {
        var (factory, file) = await StartAsync();
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");
        var before = await File.ReadAllTextAsync(file, Token);
        await Hand(factory, "dev-laptop", "claude-code", Remote("a", "https://a.homelab.example/mcp"));
        var second = await Hand(factory, "dev-laptop", "claude-code", Remote("b", "https://b.homelab.example/mcp"));
        second.Replaced.Should().Be(1);

        var group = (await GroupsAsync(admin)).Should().ContainSingle().Subject;
        group.GetProperty("server").GetProperty("name").GetString().Should().Be("b");
        var dismissed = await admin.PostAsync($"/api/v1/mcp/captures/{Key(group)}/dismiss", null, Token);

        dismissed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await dismissed.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("dismissed").GetInt32().Should().Be(1);
        (await GroupsAsync(admin)).Should().BeEmpty();
        (await File.ReadAllTextAsync(file, Token)).Should().Be(before);
        (await admin.PostAsync($"/api/v1/mcp/captures/{Key(group)}/dismiss", null, Token)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_give_the_yaml_to_paste_and_keep_the_group_when_the_catalog_is_read_only()
    {
        var (factory, file) = await StartAsync(writable: false);
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");
        var before = await File.ReadAllTextAsync(file, Token);
        await Hand(factory, "dev-laptop", "claude-code", Remote("search", "https://search.homelab.example/mcp"));
        var group = (await GroupsAsync(admin)).Should().ContainSingle().Subject;

        var response = await admin.PostAsJsonAsync($"/api/v1/mcp/captures/{Key(group)}/accept", new { version = Version(group), mode = "new", addToWorkstations = true }, Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        result.GetProperty("written").GetBoolean().Should().BeFalse();
        result.GetProperty("readOnly").GetBoolean().Should().BeTrue();
        result.GetProperty("yaml").GetString().Should().StartWith("mcpServers:").And.Contain("id: search");
        (await File.ReadAllTextAsync(file, Token)).Should().Be(before);
        (await GroupsAsync(admin)).Should().HaveCount(1);
    }

    [Fact]
    public async Task should_refuse_a_new_id_the_catalog_already_has_and_name_what_is_wrong()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");
        await Hand(factory, "dev-laptop", "claude-code", Remote("search", "https://search.homelab.example/mcp"));
        var group = (await GroupsAsync(admin)).Should().ContainSingle().Subject;

        var response = await admin.PostAsJsonAsync($"/api/v1/mcp/captures/{Key(group)}/accept", new { version = Version(group), mode = "new", id = "memory", addToWorkstations = false }, Token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("already has a server 'memory'");
        (await GroupsAsync(admin)).Should().HaveCount(1);
    }

    [Fact]
    public async Task should_not_let_an_api_key_decide_even_with_the_right_role()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");
        await Hand(factory, "dev-laptop", "claude-code", Remote("search", "https://search.homelab.example/mcp"));
        var group = (await GroupsAsync(admin)).Should().ContainSingle().Subject;
        var created = await admin.PostAsJsonAsync("/api/v1/auth/keys", new { name = "script", scope = "full" }, Token);
        var key = (await created.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("value").GetString()!;
        using var keyClient = factory.CreateClient();
        keyClient.DefaultRequestHeaders.Add("X-Api-Key", key);

        (await keyClient.GetAsync("/api/v1/mcp/captures", Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await keyClient.PostAsJsonAsync($"/api/v1/mcp/captures/{Key(group)}/accept", new { mode = "new", addToWorkstations = false }, Token)).StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
        (await keyClient.PostAsync($"/api/v1/mcp/captures/{Key(group)}/dismiss", null, Token)).StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
        (await GroupsAsync(admin)).Should().HaveCount(1);
    }

    [Theory]
    [InlineData("Operator", HttpStatusCode.OK)]
    [InlineData("Observer", HttpStatusCode.Forbidden)]
    public async Task should_let_the_operator_decide_and_not_the_observer(string role, HttpStatusCode expected)
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");
        await Hand(factory, "dev-laptop", "claude-code", Remote("search", "https://search.homelab.example/mcp"));
        var group = (await GroupsAsync(admin)).Should().ContainSingle().Subject;
        using var client = await factory.CreateSignedInClientAsync(role);

        var response = await client.PostAsync($"/api/v1/mcp/captures/{Key(group)}/dismiss", null, Token);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task should_refuse_a_capture_that_holds_a_secret_and_store_nothing()
    {
        var (factory, _) = await StartAsync();
        await using var _ = factory;
        using var admin = await factory.CreateSignedInClientAsync("Admin");

        var act = () => Hand(factory, "dev-laptop", "claude-code", Remote("search", "https://search.homelab.example/mcp", new CapturedValue("Authorization", null, "Bearer abcdefghijklmnopqrstuvwxyz")));

        (await act.Should().ThrowAsync<McpCaptureException>()).Which.Errors.Single().Should().Contain("servers[0].headers.Authorization").And.NotContain("abcdefghijklmnopqrstuvwxyz");
        (await GroupsAsync(admin)).Should().BeEmpty();
    }
}
