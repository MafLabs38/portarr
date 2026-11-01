using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog.Editing;
using Portarr.Http.Authentication;
using Portarr.Mcp.Chat;

namespace Portarr.Mcp.Test;

/// <summary>The event stream of a conversation, passed through from the runner (D-89), and the calls that end a conversation.</summary>
public sealed class ChatStreamFixture
{
    private const string Secret = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-secret";
    private const string ConversationId = "0d5c2f1e-7a3b-4c8d-9e0f-1a2b3c4d5e6f";

    private readonly StreamHandler _handler = new();
    private readonly IChatAccess _access = Substitute.For<IChatAccess>();

    private (ChatController Controller, DefaultHttpContext Http) Controller()
    {
        var options = Options.Create(new AgentOptions { Url = "http://agent.example:8488", Secret = Secret });
        _access.CanAccessAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(true);
        var runner = new AgentRunnerClient(new HttpClient(_handler), options);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Name, "alice")], "test"));
        var http = new DefaultHttpContext { User = user };
        http.Response.Body = new MemoryStream();
        var controller = new ChatController(
            runner,
            new ChatService(runner, _access, options, NullLogger<ChatService>.Instance),
            options,
            Options.Create(new McpOptions()),
            Substitute.For<ICatalogEditor>(),
            NullLogger<ChatController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return (controller, http);
    }

    private static string BodyOf(DefaultHttpContext http) => Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray());

    [Fact]
    public async Task should_pass_the_events_through_with_the_headers_that_keep_them_live()
    {
        _handler.Stream("id: 3\nevent: text\ndata: {\"seq\":3}\n\n");
        var (controller, http) = Controller();

        await controller.StreamAsync(ConversationId, after: 2, lastEventId: null, TestContext.Current.CancellationToken);

        BodyOf(http).Should().Be("id: 3\nevent: text\ndata: {\"seq\":3}\n\n");
        http.Response.ContentType.Should().Be("text/event-stream");
        http.Response.Headers.CacheControl.ToString().Should().Be("no-cache");
        http.Response.Headers["X-Accel-Buffering"].ToString().Should().Be("no");
        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Uri.Should().Be($"http://agent.example:8488/v1/conversations/{ConversationId}/events?after=2");
        request.Authorization.Should().Be($"Bearer {Secret}");
    }

    [Theory]
    [InlineData("7", 7)]
    [InlineData("0", 0)]
    public async Task should_resume_from_the_last_event_id_the_browser_sends_after_a_reconnect(string lastEventId, int expected)
    {
        _handler.Stream("");
        var (controller, _) = Controller();

        await controller.StreamAsync(ConversationId, after: 2, lastEventId, TestContext.Current.CancellationToken);

        _handler.Requests.Single().LastEventId.Should().Be(expected.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task should_ignore_a_last_event_id_that_is_not_a_sequence_number(string lastEventId)
    {
        _handler.Stream("");
        var (controller, _) = Controller();

        await controller.StreamAsync(ConversationId, after: 2, lastEventId, TestContext.Current.CancellationToken);

        _handler.Requests.Single().LastEventId.Should().BeNull();
    }

    [Fact]
    public async Task should_never_ask_for_a_negative_position()
    {
        _handler.Stream("");
        var (controller, _) = Controller();

        await controller.StreamAsync(ConversationId, after: -5, lastEventId: null, TestContext.Current.CancellationToken);

        _handler.Requests.Single().Uri.Should().EndWith("events?after=0");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, StatusCodes.Status502BadGateway)]
    public async Task should_turn_a_runner_refusal_into_not_found_or_bad_gateway(HttpStatusCode upstream, int expected)
    {
        _handler.Status = upstream;
        var (controller, http) = Controller();

        await controller.StreamAsync(ConversationId, after: 0, lastEventId: null, TestContext.Current.CancellationToken);

        http.Response.StatusCode.Should().Be(expected);
        BodyOf(http).Should().BeEmpty();
    }

    [Fact]
    public async Task should_answer_bad_gateway_when_the_runner_drops_the_stream_before_it_starts()
    {
        _handler.Fail = true;
        var (controller, http) = Controller();

        await controller.StreamAsync(ConversationId, after: 0, lastEventId: null, TestContext.Current.CancellationToken);

        http.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task should_cancel_and_delete_through_the_runner_and_forget_the_owner_of_a_deleted_conversation()
    {
        _handler.Json(HttpStatusCode.Accepted, "{}");
        var (controller, _) = Controller();

        var cancel = await controller.CancelAsync(ConversationId, new ChatCancelRequest(), TestContext.Current.CancellationToken);
        _handler.Json(HttpStatusCode.NoContent, "");
        var delete = await controller.DeleteAsync(ConversationId, TestContext.Current.CancellationToken);

        cancel.Should().BeOfType<ContentResult>().Which.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        delete.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        _handler.Requests.Select(r => $"{r.Method} {r.Uri}").Should().Equal(
            $"POST http://agent.example:8488/v1/conversations/{ConversationId}/cancel",
            $"DELETE http://agent.example:8488/v1/conversations/{ConversationId}");
        await _access.Received(1).ForgetAsync(ConversationId, Arg.Any<CancellationToken>());
    }

    private sealed record Recorded(string Method, string Uri, string? Authorization, string? LastEventId);

    private sealed class StreamHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = string.Empty;
        private string _mediaType = "text/event-stream";

        public List<Recorded> Requests { get; } = [];

        public bool Fail { get; set; }

        public HttpStatusCode Status
        {
            set => _status = value;
        }

        public void Stream(string body)
        {
            _status = HttpStatusCode.OK;
            _body = body;
            _mediaType = "text/event-stream";
        }

        public void Json(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
            _mediaType = "application/json";
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new HttpRequestException("connection refused");
            }

            Requests.Add(new Recorded(
                request.Method.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Last-Event-ID", out var ids) ? string.Join(",", ids) : null));
            // A 204 carries no body and no content type, as the runner's does.
            HttpContent content = _body.Length == 0 && _mediaType == "application/json" ? new ByteArrayContent([]) : new StringContent(_body, Encoding.UTF8, _mediaType);
            return Task.FromResult(new HttpResponseMessage(_status) { Content = content });
        }
    }
}
