using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Portarr.Reader.Test;

/// <summary>
/// The reader over real HTTP (D-100): behind one token, a window on a fake Docker that forwards only what Portarr reads,
/// and no route for anything else.
/// </summary>
public sealed class ReaderHttpFixture : IAsyncLifetime
{
    private const string Token = "reader-test-token-0123456789abcdef-0123456789";
    private const string Containers = """[{"Id":"abc","Names":["/web"],"Image":"nginx","State":"running","Status":"Up 3 hours","Created":1,"Labels":{}}]""";

    private readonly FakeHost _host = new();
    private readonly WireMockServer _docker = WireMockServer.Start();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private HttpClient _authorized = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _docker.Given(Request.Create().WithPath("/info").UsingGet()).RespondWith(Json("""{"NCPU":8,"MemTotal":16000,"Name":"secret-host","SecurityOptions":["x"]}"""));
        _docker.Given(Request.Create().WithPath("/containers/json").UsingGet()).RespondWith(Json(Containers));
        _docker.Given(Request.Create().WithPath("/containers/web/json").UsingGet()).RespondWith(Json("""{"Id":"abc","Config":{"Tty":true,"Env":["DB_PASSWORD=hunter2"]},"HostConfig":{}}"""));
        _docker.Given(Request.Create().WithPath("/containers/web/stats").UsingGet()).RespondWith(Json("""{"cpu_stats":{},"memory_stats":{"usage":1}}"""));
        _docker.Given(Request.Create().WithPath("/containers/web/logs").UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithBody("frame-bytes"));
        _docker.Given(Request.Create().WithPath("/containers/broken/json").UsingGet()).RespondWith(Response.Create().WithStatusCode(500).WithBody("secret stack trace of the engine"));
        _docker.Given(Request.Create().WithPath("/containers/gone/json").UsingGet()).RespondWith(Response.Create().WithStatusCode(404).WithBody("""{"message":"No such container: gone"}"""));

        _app = ReaderApp.Build([], Settings(_docker.Url!));
        await _app.StartAsync(Cancel);
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
        _authorized = new HttpClient { BaseAddress = _client.BaseAddress, DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", Token) } };
    }

    public async ValueTask DisposeAsync()
    {
        _authorized.Dispose();
        _client.Dispose();
        await _app.DisposeAsync();
        _docker.Dispose();
        _host.Dispose();
    }

    [Fact]
    public async Task should_keep_everything_but_the_health_check_behind_the_token()
    {
        using var anonymous = new HttpClient { BaseAddress = _client.BaseAddress };
        using var wrong = new HttpClient { BaseAddress = _client.BaseAddress, DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", "wrong") } };
        using var keyed = new HttpClient { BaseAddress = _client.BaseAddress, DefaultRequestHeaders = { { "X-Api-Key", Token } } };

        var health = await anonymous.GetAsync("/healthz", Cancel);
        var refused = await anonymous.GetAsync("/v1/info", Cancel);
        var wrongToken = await wrong.GetAsync("/v1/host", Cancel);
        var bearer = await Authorized().GetAsync("/v1/info", Cancel);
        var apiKey = await keyed.GetAsync("/v1/info", Cancel);

        health.StatusCode.Should().Be(HttpStatusCode.OK);
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refused.Headers.WwwAuthenticate.Should().ContainSingle().Which.Scheme.Should().Be("Bearer");
        wrongToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        bearer.StatusCode.Should().Be(HttpStatusCode.OK);
        apiKey.StatusCode.Should().Be(HttpStatusCode.OK);
        var info = JsonNode.Parse(await bearer.Content.ReadAsStringAsync(Cancel))!;
        info["name"]!.ToString().Should().Be("portarr-reader");
        info["capabilities"]!.AsArray().Select(c => c!.ToString()).Should().Equal("host", "docker");
        (await bearer.Content.ReadAsStringAsync(Cancel)).Should().NotContain(Token);
    }

    [Fact]
    public async Task should_serve_the_host_in_the_node_exporter_format()
    {
        var response = await Authorized().GetAsync("/v1/host", Cancel);
        var text = await response.Content.ReadAsStringAsync(Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        text.Should().Contain("node_cpu_seconds_total{cpu=\"0\",mode=\"idle\"} 3").And.Contain("node_load1 0.5");
    }

    [Fact]
    public async Task should_forward_to_docker_only_what_portarr_reads()
    {
        var client = Authorized();
        var before = _docker.LogEntries.Count();

        var info = JsonNode.Parse(await client.GetStringAsync("/v1/docker/info", Cancel))!.AsObject();
        var list = await client.GetStringAsync("/v1/docker/containers/json?all=true", Cancel);
        var inspect = await client.GetStringAsync("/v1/docker/containers/web/json", Cancel);
        var stats = await client.GetStringAsync("/v1/docker/containers/web/stats?stream=true", Cancel);
        var logs = await client.GetStringAsync("/v1/docker/containers/web/logs?tail=99999&follow=false&timestamps=true", Cancel);

        info.Select(p => p.Key).Should().BeEquivalentTo("NCPU", "MemTotal");
        list.Should().Be(Containers);
        inspect.Should().Be("""{"Id":"abc","Config":{"Tty":true}}""", "the inspect holds the container's environment: only the id and the TTY flag leave");
        stats.Should().Contain("memory_stats");
        logs.Should().Be("frame-bytes");

        var calls = _docker.LogEntries.Skip(before).Select(e => e.RequestMessage!).ToList();
        calls.Should().OnlyContain(c => c.Method == "GET", "the reader never writes to Docker");
        Query(calls, "/containers/web/stats")["stream"].Should().Be("false", "usage is always one sample, whatever the caller asks");
        Query(calls, "/containers/web/stats").AllKeys.Should().NotContain("one-shot", "it is passed on only when asked for");
        var logQuery = Query(calls, "/containers/web/logs");
        (logQuery["tail"], logQuery["follow"], logQuery["timestamps"], logQuery["stdout"], logQuery["stderr"]).Should().Be(("5000", "false", "true", "true", "true"), "the line count is capped");
    }

    [Fact]
    public async Task should_have_no_route_for_anything_else_and_never_reach_docker_for_it()
    {
        var client = Authorized();
        var before = _docker.LogEntries.Count();

        var post = await client.PostAsync("/v1/docker/containers/web/json", new StringContent("{}"), Cancel);
        var create = await client.PostAsync("/v1/docker/containers/create", new StringContent("{}"), Cancel);
        var exec = await client.GetAsync("/v1/docker/containers/web/exec", Cancel);
        var version = await client.GetAsync("/v1/docker/version", Cancel);
        var badName = await client.GetAsync("/v1/docker/containers/bad%20name/stats", Cancel);
        var hostPost = await client.PostAsync("/v1/host", new StringContent("x"), Cancel);

        post.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        create.StatusCode.Should().Be(HttpStatusCode.NotFound, "there is no route to create anything, the verb is not even looked at");
        exec.StatusCode.Should().Be(HttpStatusCode.NotFound);
        version.StatusCode.Should().Be(HttpStatusCode.NotFound);
        badName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        hostPost.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        _docker.LogEntries.Count().Should().Be(before);
    }

    [Fact]
    public async Task should_keep_dockers_not_found_and_hide_everything_else_it_says()
    {
        await using var unreachableApp = ReaderApp.Build([], Settings("http://127.0.0.1:1"));
        await unreachableApp.StartAsync(Cancel);
        using var unreachable = new HttpClient { BaseAddress = new Uri(unreachableApp.Urls.First()), DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", Token) } };

        var gone = await Authorized().GetAsync("/v1/docker/containers/gone/json", Cancel);
        var down = await unreachable.GetAsync("/v1/docker/containers/json", Cancel);

        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await gone.Content.ReadAsStringAsync(Cancel)).Should().NotContain("No such container", "Docker's own text is not forwarded");
        down.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await down.Content.ReadAsStringAsync(Cancel)).Should().Contain("Docker is not reachable");
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void should_refuse_to_start_without_a_long_token(string token)
    {
        var settings = Settings(_docker.Url!).Select(s => s.Key == "Token" ? new KeyValuePair<string, string?>("Token", token) : s);

        var start = () => ReaderApp.Build([], settings);

        start.Should().Throw<InvalidOperationException>().WithMessage("*PORTARR_READER_TOKEN*at least 32*");
    }

    [Fact]
    public async Task should_pass_one_shot_on_only_when_asked_for()
    {
        var before = _docker.LogEntries.Count();

        await Authorized().GetStringAsync("/v1/docker/containers/web/stats?one-shot=true", Cancel);
        await Authorized().GetStringAsync("/v1/docker/containers/web/stats?one-shot=nonsense", Cancel);

        var calls = _docker.LogEntries.Skip(before).Select(e => e.RequestMessage!).ToList();
        calls.Select(c => c.Query!.ContainsKey("one-shot")).Should().Equal(true, false);
        calls[0].Query!["one-shot"].Should().Equal("true");
        calls.Should().OnlyContain(c => c.Query!["stream"].Single() == "false");
    }

    [Fact]
    public async Task should_drop_from_the_container_list_every_field_portarr_does_not_read()
    {
        using var rich = WireMockServer.Start();
        rich.Given(Request.Create().WithPath("/containers/json").UsingGet()).RespondWith(Json(
            """[{"Id":"abc","Names":["/web"],"Image":"nginx","State":"running","Status":"Up","Created":1,"Labels":{"a":"b"},"Ports":[],"Mounts":[],"Command":"run --secret=hunter2","HostConfig":{"NetworkMode":"x"},"NetworkSettings":{"Networks":{}}}]"""));
        await using var app = ReaderApp.Build([], Settings(rich.Url!));
        await app.StartAsync(Cancel);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()), DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", Token) } };

        var list = JsonNode.Parse(await client.GetStringAsync("/v1/docker/containers/json?all=true", Cancel))!.AsArray();

        list.Single()!.AsObject().Select(p => p.Key).Should().Equal("Id", "Names", "Image", "State", "Status", "Created", "Labels", "Ports", "Mounts");
    }

    [Theory]
    [InlineData("-1", "0")]
    [InlineData("0", "0")]
    [InlineData("5000", "5000")]
    [InlineData("99999", "5000")]
    public async Task should_clamp_the_number_of_log_lines_whatever_is_asked(string asked, string forwarded)
    {
        var before = _docker.LogEntries.Count();

        (await Authorized().GetAsync($"/v1/docker/containers/web/logs?tail={asked}", Cancel)).StatusCode.Should().Be(HttpStatusCode.OK);

        Query(_docker.LogEntries.Skip(before).Select(e => e.RequestMessage!), "/containers/web/logs")["tail"].Should().Be(forwarded);
    }

    [Fact]
    public async Task should_refuse_a_log_count_that_is_not_a_number_without_asking_docker()
    {
        var before = _docker.LogEntries.Count();

        var response = await Authorized().GetAsync("/v1/docker/containers/web/logs?tail=abc", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _docker.LogEntries.Count().Should().Be(before);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("%2e%2e")]
    [InlineData("a%2Fb")]
    [InlineData("-leading-dash")]
    [InlineData("_leading-underscore")]
    [InlineData("name%20with%20space")]
    public async Task should_never_pass_an_odd_container_name_on_to_docker(string name)
    {
        var before = _docker.LogEntries.Count();

        var responses = new[]
        {
            await Authorized().GetAsync($"/v1/docker/containers/{name}/json", Cancel),
            await Authorized().GetAsync($"/v1/docker/containers/{name}/stats", Cancel),
            await Authorized().GetAsync($"/v1/docker/containers/{name}/logs", Cancel),
        };

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.BadRequest || r.StatusCode == HttpStatusCode.NotFound);
        _docker.LogEntries.Count().Should().Be(before);
    }

    [Fact]
    public async Task should_refuse_a_container_name_longer_than_docker_allows()
    {
        var response = await Authorized().GetAsync($"/v1/docker/containers/{new string('a', 300)}/json", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Bearer ")]
    [InlineData("Bearer")]
    [InlineData("Basic reader-test-token-0123456789abcdef-0123456789")]
    [InlineData("Bearer reader-test-token-0123456789abcdef-012345678")]
    [InlineData("Bearer reader-test-token-0123456789abcdef-0123456789x")]
    public async Task should_refuse_anything_but_the_exact_token_as_a_bearer(string authorization)
    {
        using var client = new HttpClient { BaseAddress = _client.BaseAddress };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);

        (await client.GetAsync("/v1/info", Cancel)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_accept_the_scheme_in_any_case_and_prefer_the_bearer_over_the_api_key_header()
    {
        using var lower = new HttpClient { BaseAddress = _client.BaseAddress };
        lower.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"bearer {Token}");
        using var both = new HttpClient { BaseAddress = _client.BaseAddress };
        both.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer wrong");
        both.DefaultRequestHeaders.Add("X-Api-Key", Token);

        (await lower.GetAsync("/v1/info", Cancel)).StatusCode.Should().Be(HttpStatusCode.OK, "an HTTP authentication scheme is case-insensitive");
        (await both.GetAsync("/v1/info", Cancel)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a wrong bearer is not rescued by another header");
    }

    [Fact]
    public async Task should_say_bad_gateway_when_the_host_files_cannot_be_read()
    {
        var empty = Directory.CreateTempSubdirectory("portarr-reader-empty-");
        try
        {
            await using var app = ReaderApp.Build([], Settings(_docker.Url!, proc: empty.FullName));
            await app.StartAsync(Cancel);
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()), DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", Token) } };

            var response = await client.GetAsync("/v1/host", Cancel);

            response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            (await response.Content.ReadAsStringAsync(Cancel)).Should().Contain("could not be read").And.NotContain(empty.FullName);
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task should_hide_what_docker_says_when_it_fails()
    {
        var response = await Authorized().GetAsync("/v1/docker/containers/broken/json", Cancel);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync(Cancel)).Should().NotContain("stack trace");
    }

    [Theory]
    [InlineData("ftp://docker.example")]
    [InlineData("http://user:pw@docker.example:2375")]
    [InlineData("not a url")]
    public void should_refuse_a_docker_address_that_is_not_a_socket_or_a_plain_http_url(string docker)
    {
        var options = new ReaderOptions { Token = Token, Docker = docker };

        var validate = options.Validate;

        validate.Should().Throw<InvalidOperationException>().WithMessage("*PORTARR_READER_DOCKER*");
    }

    private HttpClient Authorized() => _authorized;

    private static IResponseBuilder Json(string body) =>
        Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body);

    private List<KeyValuePair<string, string?>> Settings(string docker, string? proc = null) =>
    [
        new("Token", Token),
        new("Docker", docker),
        new("Urls", "http://127.0.0.1:0"),
        new("ProcPath", proc ?? _host.Proc),
        new("SysPath", _host.Sys),
    ];

    /// <summary>The query of the last request Docker received for a path, parsed.</summary>
    private static System.Collections.Specialized.NameValueCollection Query(IEnumerable<WireMock.IRequestMessage> calls, string path)
    {
        var call = calls.Last(c => c.Path == path);
        return System.Web.HttpUtility.ParseQueryString(new Uri(call.Url).Query);
    }
}
