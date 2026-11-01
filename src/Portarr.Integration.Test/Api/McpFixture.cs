using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Workloads;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// The MCP endpoint over Streamable HTTP (V5): off by default, then guarded by the API key, and a JSON-RPC round
/// trip through the official SDK. What the tools say is unit-tested (FleetDigestFixture, KnowledgeBaseFixture).
/// </summary>
[Trait("Category", "Integration")]
public sealed class McpFixture : IAsyncLifetime
{
    private const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-test";

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
    private PortarrFactory _enabled = null!;
    private PortarrFactory _disabled = null!;

    public McpFixture(TestDatabase database)
    {
        _database = database;
    }

    public async ValueTask InitializeAsync()
    {
        _enabled = await CreateAsync(new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:ApiKey"] = Key });
        _disabled = await CreateAsync(null);
    }

    public async ValueTask DisposeAsync()
    {
        await _enabled.DisposeAsync();
        await _disabled.DisposeAsync();
    }

    [Fact]
    public async Task should_not_serve_the_endpoint_nor_the_ui_when_off()
    {
        using var client = _disabled.CreateClient();

        var post = await client.SendAsync(Rpc("initialize", Initialize, Key), TestContext.Current.CancellationToken);
        var get = await client.GetAsync("/mcp", TestContext.Current.CancellationToken);

        // Plain 404s for everyone, never the UI and never a sign-in request.
        post.StatusCode.Should().Be(HttpStatusCode.NotFound);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/.well-known/oauth-protected-resource", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task should_refuse_a_missing_or_wrong_key(string? key)
    {
        using var client = _enabled.CreateClient();

        var response = await client.SendAsync(Rpc("initialize", Initialize, key), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle().Which.Scheme.Should().Be("Bearer");
    }

    [Fact]
    public async Task should_answer_tools_with_the_key_without_leaking_credentials()
    {
        using var client = _enabled.CreateClient();

        var initialize = await client.SendAsync(Rpc("initialize", Initialize, Key), TestContext.Current.CancellationToken);
        initialize.StatusCode.Should().Be(HttpStatusCode.OK);

        var request = Rpc("tools/call", """{"name":"get_app","arguments":{"app":"jellyfin"}}""", null);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        var call = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await ReadResultTextAsync(call);

        text.Should().Contain("https://jellyfin.homelab.example/").And.NotContain("token=abc").And.NotContain("#top");
    }

    [Fact]
    public async Task should_list_and_serve_only_what_the_role_behind_the_key_may_use()
    {
        using var admin = await _enabled.CreateSignedInClientAsync();
        var role = await admin.PostAsJsonAsync("/api/v1/roles", new { name = "Apps only", permissions = new[] { "apps.view" } }, TestContext.Current.CancellationToken);
        role.EnsureSuccessStatusCode();
        using var appsOnly = await _enabled.CreateSignedInClientAsync("Apps only");
        var created = await appsOnly.PostAsJsonAsync("/api/v1/auth/keys", new { name = "mcp", scope = "full" }, TestContext.Current.CancellationToken);
        var key = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("value").GetString()!;
        using var anonymous = _enabled.CreateClient();

        var tools = await ResultAsync(anonymous, "tools/list", "{}", key);
        var resources = await ResultAsync(anonymous, "resources/list", "{}", key);
        var allowed = await ResultAsync(anonymous, "tools/call", """{"name":"list_apps","arguments":{}}""", key);
        var denied = await ResultAsync(anonymous, "tools/call", """{"name":"get_overview","arguments":{}}""", key);

        tools.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).Should().BeEquivalentTo("list_apps", "get_app");
        resources.GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("name").GetString()).Should().NotContain("infrastructure");
        IsError(allowed).Should().BeFalse();
        IsError(denied).Should().BeTrue();
        denied.GetProperty("content")[0].GetProperty("text").GetString().Should().Contain("lacks");
    }

    private static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var flag) && flag.GetBoolean();

    private static async Task<JsonElement> ResultAsync(HttpClient client, string method, string parameters, string key)
    {
        using var response = await client.SendAsync(Rpc(method, parameters, key), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal))?["data: ".Length..] ?? body;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("result").Clone();
    }

    [Fact]
    public async Task should_report_its_state_to_the_ui_without_the_key()
    {
        using var client = await _enabled.CreateSignedInClientAsync();

        var body = await client.GetStringAsync("/api/v1/assistant", TestContext.Current.CancellationToken);

        body.Should().Contain("\"enabled\":true").And.Contain("get_overview").And.NotContain(Key);
    }

    private const string Initialize = """{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}""";

    private static HttpRequestMessage Rpc(string method, string parameters, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent($$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{parameters}}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        if (key is not null)
        {
            request.Headers.Add("X-Api-Key", key);
        }

        return request;
    }

    /// <summary>The first text content of a tools/call answer, sent as JSON or as one server-sent event.</summary>
    private static async Task<string> ReadResultTextAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal))?["data: ".Length..] ?? body;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
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
