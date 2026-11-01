using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Workloads;

namespace Portarr.Integration.Test.Api;

/// <summary>The two hosts of McpFixture, built once for the class: the MCP server on, and off.</summary>
public sealed class McpHosts : IAsyncLifetime
{
    public const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-test";

    private const string Catalog = """
        environments:
          - { id: server-1, name: Server 1, docker: { url: "http://socket-proxy:2375" } }
        apps:
          - id: jellyfin
            name: Jellyfin
            urls: [{ label: Public, href: "https://jellyfin.homelab.example/?token=abc#top" }]
            workloads: [{ runtime: docker@server-1, match: { name: jellyfin } }]
        """;

    private readonly TestDatabase _database;

    public McpHosts(TestDatabase database)
    {
        _database = database;
    }

    public PortarrFactory Enabled { get; private set; } = null!;

    public PortarrFactory Disabled { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Enabled = await CreateAsync(new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:ApiKey"] = Key });
        Disabled = await CreateAsync(null);
    }

    public async ValueTask DisposeAsync()
    {
        await Enabled.DisposeAsync();
        await Disabled.DisposeAsync();
    }

    private async Task<PortarrFactory> CreateAsync(IReadOnlyDictionary<string, string?>? settings)
    {
        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), Catalog);
        var factory = new PortarrFactory(options, settings);
        factory.Workloads.Workloads["server-1"] = [FakeWorkloadSource.Container("server-1", "jellyfin")];
        await factory.Services.GetRequiredService<IWorkloadInventory>().RefreshAsync(TestContext.Current.CancellationToken);
        return factory;
    }
}

/// <summary>
/// The MCP endpoint over Streamable HTTP (V5): off by default, then guarded by the API key, and a JSON-RPC round
/// trip through the official SDK. What the tools say is unit-tested (FleetDigestFixture, KnowledgeBaseFixture).
/// </summary>
[Trait("Category", "Integration")]
public sealed class McpFixture : IClassFixture<McpHosts>
{
    private const string Key = McpHosts.Key;

    private readonly McpHosts _hosts;

    public McpFixture(McpHosts hosts)
    {
        _hosts = hosts;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task should_not_serve_the_endpoint_nor_the_ui_when_off()
    {
        using var client = _hosts.Disabled.CreateClient();

        using var post = await client.SendAsync(McpTestClient.Request("initialize", McpTestClient.Initialize, Key), Token);
        var get = await client.GetAsync("/mcp", Token);

        // Plain 404s for everyone, never the UI and never a sign-in request.
        post.StatusCode.Should().Be(HttpStatusCode.NotFound);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/.well-known/oauth-protected-resource", Token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task should_refuse_a_missing_or_wrong_key(string? key)
    {
        using var client = _hosts.Enabled.CreateClient();

        using var response = await client.SendAsync(McpTestClient.Request("initialize", McpTestClient.Initialize, key), Token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle().Which.Scheme.Should().Be("Bearer");
    }

    [Fact]
    public async Task should_prefer_the_api_key_header_to_a_bearer_token()
    {
        using var client = _hosts.Enabled.CreateClient();
        using var request = McpTestClient.Request("initialize", McpTestClient.Initialize, Key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-key");

        using var response = await client.SendAsync(request, Token);

        // The X-Api-Key header is read first (ApiKeys.Read): the bearer is only a fallback, so the valid header wins.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_answer_tools_with_the_key_without_leaking_credentials()
    {
        using var client = _hosts.Enabled.CreateClient();
        using var initialize = await client.SendAsync(McpTestClient.Request("initialize", McpTestClient.Initialize, Key), Token);
        initialize.StatusCode.Should().Be(HttpStatusCode.OK);

        using var request = McpTestClient.Request("tools/call", """{"name":"get_app","arguments":{"app":"jellyfin"}}""", null);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        using var call = await client.SendAsync(request, Token);
        call.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = McpTestClient.TextOf(await ReadAsync(call));

        text.Should().Contain("https://jellyfin.homelab.example/").And.NotContain("token=abc").And.NotContain("#top");
    }

    [Fact]
    public async Task should_list_and_serve_only_what_the_role_behind_the_key_may_use()
    {
        using var admin = await _hosts.Enabled.CreateSignedInClientAsync();
        var role = await admin.PostAsJsonAsync("/api/v1/roles", new { name = "Apps only", permissions = new[] { "apps.view" } }, Token);
        role.EnsureSuccessStatusCode();
        using var appsOnly = await _hosts.Enabled.CreateSignedInClientAsync("Apps only");
        var created = await appsOnly.PostAsJsonAsync("/api/v1/auth/keys", new { name = "mcp", scope = "full" }, Token);
        var key = (await created.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("value").GetString()!;
        using var anonymous = _hosts.Enabled.CreateClient();

        var tools = (await McpTestClient.SendAsync(anonymous, "tools/list", "{}", key)).GetProperty("result");
        var resources = (await McpTestClient.SendAsync(anonymous, "resources/list", "{}", key)).GetProperty("result");
        var allowed = await McpTestClient.CallRawAsync(anonymous, "list_apps", "{}", key);
        var denied = await McpTestClient.CallRawAsync(anonymous, "get_overview", "{}", key);

        tools.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).Should().BeEquivalentTo("list_apps", "get_app");
        resources.GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("name").GetString()).Should().NotContain("infrastructure");
        McpTestClient.IsError(allowed).Should().BeFalse();
        McpTestClient.IsError(denied).Should().BeTrue();
        McpTestClient.TextOf(denied).Should().Contain("lacks");
    }

    [Fact]
    public async Task should_report_its_state_to_the_ui_without_the_key()
    {
        using var client = await _hosts.Enabled.CreateSignedInClientAsync();

        var body = await client.GetStringAsync("/api/v1/assistant", Token);

        body.Should().Contain("\"enabled\":true").And.Contain("get_overview").And.NotContain(Key);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        var json = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal))?["data: ".Length..] ?? body;
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
