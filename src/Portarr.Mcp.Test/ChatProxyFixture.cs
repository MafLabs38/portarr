using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portarr.Mcp.Chat;
using Portarr.Mcp.Server;

namespace Portarr.Mcp.Test;

/// <summary>
/// The built-in chat's proxy (D-89): off without a runner URL, the shared secret on every call to the runner and
/// nothing of the browser's request forwarded, inputs checked before they leave Portarr; and the chat's origin in
/// the audit (only when shaped like one).
/// </summary>
public sealed class ChatProxyFixture
{
    private const string Secret = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-secret";
    private const string ConversationId = "0d5c2f1e-7a3b-4c8d-9e0f-1a2b3c4d5e6f";

    private readonly RecordingHandler _handler = new();

    private ChatController Controller(string? url = "http://agent.example:8488")
    {
        var options = Options.Create(new AgentOptions { Url = url, Secret = Secret });
        var controller = new ChatController(new AgentRunnerClient(new HttpClient(_handler), options), options, NullLogger<ChatController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.Request.Headers.Cookie = "session=browser-cookie";
        return controller;
    }

    [Fact]
    public async Task should_be_off_without_a_runner_url()
    {
        var controller = Controller(url: null);

        (await controller.GetAsync(TestContext.Current.CancellationToken)).Enabled.Should().BeFalse();
        (await controller.ListAsync(TestContext.Current.CancellationToken)).Should().BeOfType<NotFoundResult>();
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_send_the_shared_secret_and_nothing_from_the_browser()
    {
        _handler.Respond(HttpStatusCode.Accepted, """{"accepted":true}""");

        var result = await Controller().SendAsync(ConversationId, new ChatMessageRequest("Add a note\nline two", "opus"), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ContentResult>().Which.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Uri.Should().Be($"http://agent.example:8488/v1/conversations/{ConversationId}/messages");
        request.Authorization.Should().Be($"Bearer {Secret}");
        request.Cookie.Should().BeNull();
        request.Body.Should().Be("""{"text":"Add a note\nline two","model":"opus"}""");
    }

    [Theory]
    [InlineData("not-a-conversation", "hello", null)]
    [InlineData(ConversationId, "   ", null)]
    [InlineData(ConversationId, "hello", "Opus; rm")]
    public async Task should_refuse_bad_input_before_calling_the_runner(string id, string text, string? model)
    {
        var result = await Controller().SendAsync(id, new ChatMessageRequest(text, model), TestContext.Current.CancellationToken);

        result.Should().Match<IActionResult>(r => r is NotFoundResult || (r is ObjectResult && ((ObjectResult)r).StatusCode == StatusCodes.Status400BadRequest));
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task should_turn_a_refused_secret_into_a_bad_gateway()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, """{"error":"Missing or wrong secret."}""");

        var result = await Controller().AnswerAsync(ConversationId, "req-1", new ChatPermissionRequest(true), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task should_say_the_runner_is_unreachable()
    {
        _handler.Fail = true;

        var status = await Controller().GetAsync(TestContext.Current.CancellationToken);

        status.Should().Be(new ChatStatusResource(true, false, null));
    }

    [Theory]
    [InlineData("chat:" + ConversationId, "chat:" + ConversationId)]
    [InlineData("chat", "chat")]
    [InlineData("chat:../../etc", null)]
    [InlineData("Chat", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void should_keep_only_an_origin_shaped_header(string? header, string? expected)
    {
        CatalogTools.OriginOf(header).Should().Be(expected);
    }

    private sealed record RecordedRequest(string Uri, string? Authorization, string? Cookie, string? Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _json = "[]";

        public List<RecordedRequest> Requests { get; } = [];

        public bool Fail { get; set; }

        public void Respond(HttpStatusCode status, string json)
        {
            _status = status;
            _json = json;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new HttpRequestException("connection refused");
            }

            Requests.Add(new RecordedRequest(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join(";", cookies) : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(_status) { Content = new StringContent(_json, Encoding.UTF8, "application/json") };
        }
    }
}
