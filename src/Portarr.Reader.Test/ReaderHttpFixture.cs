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

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _docker.Given(Request.Create().WithPath("/info").UsingGet()).RespondWith(Json("""{"NCPU":8,"MemTotal":16000,"Name":"secret-host","SecurityOptions":["x"]}"""));
        _docker.Given(Request.Create().WithPath("/containers/json").UsingGet()).RespondWith(Json(Containers));
        _docker.Given(Request.Create().WithPath("/containers/web/json").UsingGet()).RespondWith(Json("""{"Id":"abc","Config":{"Tty":true,"Env":["DB_PASSWORD=hunter2"]},"HostConfig":{}}"""));
        _docker.Given(Request.Create().WithPath("/containers/web/stats").UsingGet()).RespondWith(Json("""{"cpu_stats":{},"memory_stats":{"usage":1}}"""));
        _docker.Given(Request.Create().WithPath("/containers/web/logs").UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithBody("frame-bytes"));
        _docker.Given(Request.Create().WithPath("/containers/gone/json").UsingGet()).RespondWith(Response.Create().WithStatusCode(404).WithBody("""{"message":"No such container: gone"}"""));

        _app = ReaderApp.Build([], Settings(_docker.Url!));
        await _app.StartAsync(Cancel);
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async ValueTask DisposeAsync()
    {
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
        calls.Select(c => c.Url).Should().Contain(u => u.Contains("/containers/web/stats?stream=false", StringComparison.Ordinal), "usage is always one sample, whatever the caller asks");
        calls.Select(c => c.Url).Should().Contain(u => u.Contains("/logs?stdout=true&stderr=true&timestamps=true&tail=5000&follow=false", StringComparison.Ordinal), "the line count is capped");
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
        create.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        exec.StatusCode.Should().Be(HttpStatusCode.NotFound);
        version.StatusCode.Should().Be(HttpStatusCode.NotFound);
        badName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        hostPost.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        _docker.LogEntries.Count().Should().Be(before);
    }

    [Fact]
    public async Task should_keep_dockers_not_found_and_hide_everything_else_it_says()
    {
        using var unreachableApp = ReaderApp.Build([], Settings("http://127.0.0.1:1"));
        await unreachableApp.StartAsync(Cancel);
        using var unreachable = new HttpClient { BaseAddress = new Uri(unreachableApp.Urls.First()), DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", Token) } };

        var gone = await Authorized().GetAsync("/v1/docker/containers/gone/json", Cancel);
        var down = await unreachable.GetAsync("/v1/docker/containers/json", Cancel);
        await unreachableApp.StopAsync(Cancel);

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

    private HttpClient Authorized()
    {
        var client = new HttpClient { BaseAddress = _client.BaseAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    private static IResponseBuilder Json(string body) =>
        Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(body);

    private List<KeyValuePair<string, string?>> Settings(string docker) =>
    [
        new("Token", Token),
        new("Docker", docker),
        new("Urls", "http://127.0.0.1:0"),
        new("ProcPath", _host.Proc),
        new("SysPath", _host.Sys),
    ];
}
