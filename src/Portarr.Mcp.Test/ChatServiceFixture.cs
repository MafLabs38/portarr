using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portarr.Core.Accounts;
using Portarr.Mcp.Chat;

namespace Portarr.Mcp.Test;

/// <summary>The rules of the per-user chat: who sees which conversation, and what happens when the owner cannot be recorded.</summary>
public sealed class ChatServiceFixture
{
    private const string Mine = "0d5c2f1e-7a3b-4c8d-9e0f-1a2b3c4d5e6f";
    private const string Theirs = "1d5c2f1e-7a3b-4c8d-9e0f-1a2b3c4d5e6f";
    private const string Nobodys = "2d5c2f1e-7a3b-4c8d-9e0f-1a2b3c4d5e6f";

    private readonly ScriptedHandler _handler = new();
    private readonly IChatAccess _access = Substitute.For<IChatAccess>();

    private ChatService Subject()
    {
        var options = Options.Create(new AgentOptions { Url = "http://agent.example:8488", Secret = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-secret" });
        return new ChatService(new AgentRunnerClient(new HttpClient(_handler), options), _access, options, NullLogger<ChatService>.Instance);
    }

    [Theory]
    [InlineData(false, new[] { Mine })]
    [InlineData(true, new[] { Mine, Nobodys })]
    public async Task should_list_the_callers_conversations_and_for_a_manager_the_ownerless_ones(bool manager, string[] expected)
    {
        _handler.Reply(HttpMethod.Get, HttpStatusCode.OK, $$"""[{"id":"{{Mine}}"},{"id":"{{Theirs}}"},{"id":"{{Nobodys}}"}]""");
        _access.ListAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, ChatOwnership>
        {
            [Mine] = new(Mine, 7, "alice"),
            [Theirs] = new(Theirs, 8, "bob"),
        });

        var response = await Subject().ListAsync(new ChatCaller(7, "alice", manager), TestContext.Current.CancellationToken);

        System.Text.Json.Nodes.JsonNode.Parse(response.Json!)!.AsArray().Select(n => n!["id"]!.GetValue<string>()).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task should_remove_the_runners_conversation_when_its_owner_cannot_be_recorded()
    {
        _handler.Reply(HttpMethod.Post, HttpStatusCode.Created, $$"""{"id":"{{Mine}}"}""");
        _handler.Reply(HttpMethod.Delete, HttpStatusCode.NoContent, "{}");
        _access.RegisterAsync(Mine, 7, "alice", Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("database is down")));

        var create = () => Subject().CreateAsync(new { }, new ChatCaller(7, "alice", false), TestContext.Current.CancellationToken);

        await create.Should().ThrowAsync<InvalidOperationException>();
        _handler.Calls.Should().Contain($"DELETE http://agent.example:8488/v1/conversations/{Mine}");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.BadGateway, false)]
    public async Task should_forget_the_owner_once_the_runner_no_longer_has_the_conversation(HttpStatusCode status, bool forgotten)
    {
        _handler.Reply(HttpMethod.Delete, status, "{}");

        await Subject().DeleteAsync(Mine, TestContext.Current.CancellationToken);

        await _access.Received(forgotten ? 1 : 0).ForgetAsync(Mine, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-a-conversation")]
    [InlineData("../../etc/passwd")]
    public async Task should_not_reach_the_ownership_check_with_a_malformed_id(string id)
    {
        (await Subject().CanAccessAsync(id, new ChatCaller(7, "alice", true), TestContext.Current.CancellationToken)).Should().BeFalse();
        await _access.DidNotReceiveWithAnyArgs().CanAccessAsync(default!, default, default, TestContext.Current.CancellationToken);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<HttpMethod, (HttpStatusCode Status, string Json)> _replies = [];

        public List<string> Calls { get; } = [];

        public void Reply(HttpMethod method, HttpStatusCode status, string json) => _replies[method] = (status, json);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add($"{request.Method} {request.RequestUri}");
            var (status, json) = _replies.GetValueOrDefault(request.Method, (HttpStatusCode.OK, "{}"));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
