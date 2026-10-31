using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// The built-in chat per user (V7, D-98), over a fake runner: a conversation belongs to who created it, a user manager
/// lists them all, approving a change needs <c>catalog.write</c>, and the message carries a token tied to the user that
/// opens the MCP endpoint (and only it).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ChatOwnershipFixture
{
    private const string Secret = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-secret";
    private const string Initialize = """{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}""";

    private readonly TestDatabase _database;

    public ChatOwnershipFixture(TestDatabase database)
    {
        _database = database;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task should_limit_conversations_to_their_owner_and_let_managers_list_them_all()
    {
        var runner = new FakeRunner();
        await using var factory = await FactoryAsync(runner);
        using var admin = await factory.CreateSignedInClientAsync();
        using var operatorClient = await factory.CreateSignedInClientAsync("Operator");

        var mine = await CreateAsync(operatorClient);
        var theirs = await CreateAsync(admin);

        (await IdsAsync(operatorClient)).Should().BeEquivalentTo([mine]);
        var seenByAdmin = await admin.GetFromJsonAsync<JsonArray>("/api/v1/assistant/chat/conversations", Token);
        seenByAdmin!.Select(n => (n!["id"]!.GetValue<string>(), n["owner"]!.GetValue<string>())).Should().BeEquivalentTo([(mine, "operator-user"), (theirs, "admin")]);

        foreach (var (method, path) in new[] { ("GET", ""), ("POST", "/messages"), ("POST", "/cancel"), ("GET", "/events"), ("DELETE", "") })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/assistant/chat/conversations/{theirs}{path}") { Content = method == "POST" ? JsonContent.Create(new { text = "hello" }) : null };
            (await operatorClient.SendAsync(request, Token)).StatusCode.Should().Be(HttpStatusCode.NotFound, $"{method} {path}");
        }

        (await admin.GetAsync($"/api/v1/assistant/chat/conversations/{mine}", Token)).StatusCode.Should().Be(HttpStatusCode.NotFound, "managers list others' conversations, they do not read them");
        (await operatorClient.DeleteAsync($"/api/v1/assistant/chat/conversations/{mine}", Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await IdsAsync(operatorClient)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_hand_the_runner_a_token_that_opens_the_mcp_endpoint_only()
    {
        var runner = new FakeRunner();
        await using var factory = await FactoryAsync(runner);
        using var operatorClient = await factory.CreateSignedInClientAsync("Operator");
        var id = await CreateAsync(operatorClient);

        (await operatorClient.PostAsJsonAsync($"/api/v1/assistant/chat/conversations/{id}/messages", new { text = "list the stacks" }, Token)).EnsureSuccessStatusCode();
        var token = JsonNode.Parse(runner.LastMessage!)!["mcpToken"]!.GetValue<string>();

        token.Should().StartWith("ptc_");
        using var anonymous = factory.CreateClient();
        using var mcp = Rpc(token);
        (await anonymous.SendAsync(mcp, Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        anonymous.DefaultRequestHeaders.Add("X-Api-Key", token);
        (await anonymous.GetAsync("/api/v1/apps", Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a chat token is for /mcp only");

        factory.Clock.Advance(TimeSpan.FromHours(1));
        using var late = factory.CreateClient();
        (await late.SendAsync(Rpc(token), Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "it is short-lived");
    }

    [Fact]
    public async Task should_let_anyone_with_chat_reject_but_only_catalog_writers_approve()
    {
        var runner = new FakeRunner();
        await using var factory = await FactoryAsync(runner);
        using var admin = await factory.CreateSignedInClientAsync();
        (await admin.PostAsJsonAsync("/api/v1/roles", new { name = "Chatter", permissions = new[] { "apps.view", "chat.use" } }, Token)).EnsureSuccessStatusCode();
        var roles = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/roles", Token)).GetProperty("roles").EnumerateArray().First(r => r.GetProperty("name").GetString() == "Chatter");
        (await admin.PostAsJsonAsync("/api/v1/users", new { username = "chatter-user", password = PortarrFactory.Password, roleId = roles.GetProperty("id").GetInt32() }, Token)).EnsureSuccessStatusCode();
        using var chatter = await factory.CreateSignedInClientAsync("Chatter");
        var id = await CreateAsync(chatter);
        var path = $"/api/v1/assistant/chat/conversations/{id}/permissions/req-1";

        (await chatter.PostAsJsonAsync(path, new { approve = true }, Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await chatter.PostAsJsonAsync(path, new { approve = false }, Token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<PortarrFactory> FactoryAsync(FakeRunner runner)
    {
        var factory = new PortarrFactory(
            await _database.CreateOptionsAsync(),
            new Dictionary<string, string?> { ["Agent:Url"] = "http://agent.example:8488", ["Agent:Secret"] = Secret, ["Mcp:Enabled"] = "true" },
            services => services.AddHttpClient<Portarr.Mcp.Chat.AgentRunnerClient>().ConfigurePrimaryHttpMessageHandler(() => runner));
        _ = factory.Services;
        return factory;
    }

    private static async Task<string> CreateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/assistant/chat/conversations", new { }, Token);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetString()!;
    }

    private static async Task<List<string>> IdsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonArray>("/api/v1/assistant/chat/conversations", Token))!.Select(n => n!["id"]!.GetValue<string>()).ToList();

    private static HttpRequestMessage Rpc(string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent($$"""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{{Initialize}}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        request.Headers.Add("X-Api-Key", key);
        return request;
    }

    /// <summary>A runner that remembers its conversations and the last message it was sent.</summary>
    private sealed class FakeRunner : HttpMessageHandler
    {
        private readonly List<string> _ids = [];

        public string? LastMessage { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/v1/conversations")
            {
                var id = Guid.NewGuid().ToString();
                _ids.Add(id);
                return Json(HttpStatusCode.Created, $$"""{"id":"{{id}}"}""");
            }

            if (request.Method == HttpMethod.Get && path == "/v1/conversations")
            {
                return Json(HttpStatusCode.OK, "[" + string.Join(",", _ids.Select(i => $$"""{"id":"{{i}}"}""")) + "]");
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/messages", StringComparison.Ordinal))
            {
                LastMessage = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json(HttpStatusCode.Accepted, """{"accepted":true}""");
            }

            if (request.Method == HttpMethod.Delete)
            {
                _ids.Remove(path.Split('/')[^1]);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return Json(HttpStatusCode.OK, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
