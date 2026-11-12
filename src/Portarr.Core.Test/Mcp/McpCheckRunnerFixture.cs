using System.Text;
using System.Net;
using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Mcp;
using Portarr.Core.Test.Checks;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Mcp;

public sealed class McpCheckRunnerFixture : CoreTest<McpCheckRunner>
{
    private const string Result = """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","serverInfo":{"name":"memory","version":"1.0"}}}""";

    private static readonly Uri Target = new("https://memory.homelab.example/mcp");

    private static readonly CheckDefinition Definition = new(Target, CheckDefinition.DefaultExpect, null, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10), true, CheckTypes.Mcp);

    [Fact]
    public async Task should_send_one_initialize_request_that_accepts_json_and_event_streams()
    {
        string? sent = null;
        var handler = new FakeHttpHandler(async (request, token) =>
        {
            sent = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Result) };
        });
        Given(handler);

        var result = await Subject.RunAsync(Target, Definition, TestContext.Current.CancellationToken);

        result.Up.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Headers.ConnectionClose.Should().BeTrue();
        request.Headers.Accept.Select(a => a.MediaType).Should().BeEquivalentTo("application/json", "text/event-stream");
        sent.Should().Contain("\"method\":\"initialize\"");
        request.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task should_be_up_on_an_event_stream_answer()
    {
        Given(Answer(HttpStatusCode.OK, "event: message\ndata: " + Result + "\n\n", "text/event-stream"));

        (await Subject.RunAsync(Target, Definition, TestContext.Current.CancellationToken)).Up.Should().BeTrue();
    }

    [Fact]
    public async Task should_be_up_when_the_server_answers_with_a_json_rpc_error()
    {
        // A server that refuses our protocol version is still an MCP server, and alive.
        Given(Answer(HttpStatusCode.BadRequest, """{"jsonrpc":"2.0","id":1,"error":{"code":-32602,"message":"Unsupported protocol version"}}"""));

        var result = await Subject.RunAsync(Target, Definition, TestContext.Current.CancellationToken);

        result.Up.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task should_be_up_when_the_server_asks_for_credentials(HttpStatusCode status)
    {
        Given(Answer(status, "Unauthorized"));

        var result = await Subject.RunAsync(Target, Definition, TestContext.Current.CancellationToken);

        result.Up.Should().BeTrue();
        result.StatusCode.Should().Be((int)status);
    }

    [Fact]
    public async Task should_fail_when_a_200_answer_is_not_an_mcp_message()
    {
        Given(Answer(HttpStatusCode.OK, "<html>Welcome</html>", "text/html"));

        (await Subject.RunAsync(Target, Definition, TestContext.Current.CancellationToken)).Error.Should().Be(CheckErrors.InvalidResponse);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "http-502")]
    [InlineData(HttpStatusCode.NotFound, "http-404")]
    public async Task should_fail_with_the_status_when_the_answer_is_not_an_mcp_message(HttpStatusCode status, string expected)
    {
        Given(Answer(status, "nope"));

        (await Subject.RunAsync(Target, Definition, TestContext.Current.CancellationToken)).Error.Should().Be(expected);
    }

    [Fact]
    public async Task should_time_out_after_the_check_timeout()
    {
        Given(new FakeHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var running = Subject.RunAsync(Target, Definition, TestContext.Current.CancellationToken);
        Clock.Advance(Definition.Timeout);

        (await running).Error.Should().Be(CheckErrors.Timeout);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, "dns-failure")]
    [InlineData(HttpRequestError.ConnectionError, "connection-failed")]
    [InlineData(HttpRequestError.SecureConnectionError, "tls-invalid")]
    public async Task should_report_a_reason_code_never_the_exception_message(HttpRequestError error, string expected)
    {
        Given(new FakeHttpHandler((_, _) => throw new HttpRequestException(error, "Name or service not known (memory.homelab.example:443)")));

        (await Subject.RunAsync(Target, Definition, TestContext.Current.CancellationToken)).Error.Should().Be(expected);
    }

    [Fact]
    public async Task should_close_the_session_the_server_opened()
    {
        var handler = new FakeHttpHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Result) };
            if (request.Method == HttpMethod.Post)
            {
                response.Headers.Add(McpCheckRunner.SessionHeader, "abc123");
            }

            return Task.FromResult(response);
        });
        Given(handler);

        await Subject.RunAsync(Target, Definition, TestContext.Current.CancellationToken);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Method.Should().Be(HttpMethod.Delete);
        handler.Requests[1].Headers.GetValues(McpCheckRunner.SessionHeader).Should().Equal("abc123");
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"result":{}}""", true)]
    [InlineData("""{"jsonrpc":"2.0","id":1,"error":{"code":-1}}""", true)]
    [InlineData("event: message\r\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}\r\n\r\n", true)]
    [InlineData("""{"jsonrpc":"1.0","id":1}""", false)]
    [InlineData("""{"hello":"world"}""", false)]
    [InlineData("""[{"jsonrpc":"2.0"}]""", false)]
    [InlineData("<html></html>", false)]
    [InlineData("data: not json", false)]
    [InlineData("", false)]
    public void should_recognise_a_json_rpc_message(string body, bool expected)
    {
        McpCheckRunner.IsJsonRpc(body).Should().Be(expected);
    }

    private static FakeHttpHandler Answer(HttpStatusCode status, string body, string mediaType = "application/json")
    {
        return new FakeHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType) }));
    }

    private void Given(FakeHttpHandler handler)
    {
        Mocker.Get<IHttpClientFactory>().CreateClient(HttpCheckRunner.HttpClientName).Returns(_ => new HttpClient(handler));
    }

    [Theory]
    [InlineData("data: {}\n\n", true)]
    [InlineData("data: {}\r\n\r\n", true)]
    [InlineData("data: {}\r\r", true)]
    [InlineData("data: {}\n", false)]
    [InlineData("data: {}\r\n", false)]
    public void should_know_where_a_server_sent_event_ends(string text, bool ends)
    {
        McpCheckRunner.EndsAnEvent(Encoding.UTF8.GetBytes(text)).Should().Be(ends);
    }
}
