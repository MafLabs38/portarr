using System.Net;
using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Checks;

public sealed class HttpCheckRunnerFixture : CoreTest<HttpCheckRunner>
{
    private static readonly Uri Target = new("https://app.homelab.example/");

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Found)]
    public async Task should_be_up_on_2xx_and_3xx_with_the_status_and_latency(HttpStatusCode status)
    {
        Given(new FakeHttpHandler(async (_, _) =>
        {
            Clock.Advance(TimeSpan.FromMilliseconds(140));
            await Task.Yield();
            return new HttpResponseMessage(status);
        }));

        var result = await Subject.RunAsync(Target, CheckDefinition.Default, TestContext.Current.CancellationToken);

        result.Up.Should().BeTrue();
        result.StatusCode.Should().Be((int)status);
        result.Latency.Should().Be(TimeSpan.FromMilliseconds(140));
        result.At.Should().Be(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task should_fail_with_the_status_code_outside_the_expected_ones()
    {
        Given(Answer(HttpStatusCode.BadGateway));

        var result = await Subject.RunAsync(Target, CheckDefinition.Default, TestContext.Current.CancellationToken);

        result.Error.Should().Be("http-502");
        result.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task should_accept_custom_expected_statuses()
    {
        Given(Answer(HttpStatusCode.Unauthorized));
        var definition = CheckDefinition.Default with { Expect = [new(200, 200), new(401, 401)] };

        var result = await Subject.RunAsync(Target, definition, TestContext.Current.CancellationToken);

        result.Up.Should().BeTrue();
    }

    [Theory]
    [InlineData("<h1>Welcome to Grafana</h1>", true)]
    [InlineData("<h1>502 Bad Gateway</h1>", false)]
    public async Task should_look_for_the_keyword_in_the_body(string body, bool up)
    {
        Given(Answer(HttpStatusCode.OK, body));

        var result = await Subject.RunAsync(Target, CheckDefinition.Default with { Keyword = "Grafana" }, TestContext.Current.CancellationToken);

        result.Up.Should().Be(up);
        result.Error.Should().Be(up ? null : "keyword-missing");
    }

    [Fact]
    public async Task should_only_search_the_first_64_kib_of_the_body()
    {
        Given(Answer(HttpStatusCode.OK, new string('a', HttpCheckRunner.MaxBodyBytes) + "Grafana"));

        var result = await Subject.RunAsync(Target, CheckDefinition.Default with { Keyword = "Grafana" }, TestContext.Current.CancellationToken);

        result.Error.Should().Be("keyword-missing");
    }

    [Fact]
    public async Task should_time_out_after_the_check_timeout()
    {
        Given(new FakeHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var running = Subject.RunAsync(Target, CheckDefinition.Default, TestContext.Current.CancellationToken);
        Clock.Advance(CheckDefinition.DefaultTimeout);
        var result = await running;

        result.Error.Should().Be("timeout");
        result.StatusCode.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, "dns-failure")]
    [InlineData(HttpRequestError.ConnectionError, "connection-failed")]
    [InlineData(HttpRequestError.SecureConnectionError, "tls-invalid")]
    [InlineData(HttpRequestError.InvalidResponse, "invalid-response")]
    public async Task should_report_a_reason_code_never_the_exception_message(HttpRequestError error, string expected)
    {
        Given(new FakeHttpHandler((_, _) => throw new HttpRequestException(error, "Name or service not known (app.homelab.example:443)")));

        var result = await Subject.RunAsync(Target, CheckDefinition.Default, TestContext.Current.CancellationToken);

        result.Error.Should().Be(expected);
    }

    [Fact]
    public async Task should_report_an_expired_certificate_seen_during_the_handshake()
    {
        Given(new FakeHttpHandler((_, _) => throw new HttpRequestException(HttpRequestError.SecureConnectionError))
        {
            CertificateExpiresAt = Clock.GetUtcNow().AddDays(-1),
        });

        var result = await Subject.RunAsync(Target, CheckDefinition.Default, TestContext.Current.CancellationToken);

        result.Error.Should().Be("tls-expired");
        result.CertificateExpiresAt.Should().Be(Clock.GetUtcNow().AddDays(-1));
    }

    [Fact]
    public async Task should_record_the_certificate_expiry_and_open_a_new_connection_each_time()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))
        {
            CertificateExpiresAt = Clock.GetUtcNow().AddDays(60),
        };
        Given(handler);

        var result = await Subject.RunAsync(Target, CheckDefinition.Default, TestContext.Current.CancellationToken);

        result.CertificateExpiresAt.Should().Be(Clock.GetUtcNow().AddDays(60));
        handler.Requests.Should().ContainSingle().Which.Headers.ConnectionClose.Should().BeTrue();
    }

    private static FakeHttpHandler Answer(HttpStatusCode status, string body = "")
    {
        return new FakeHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));
    }

    private void Given(FakeHttpHandler handler)
    {
        Mocker.Get<IHttpClientFactory>().CreateClient(HttpCheckRunner.HttpClientName).Returns(_ => new HttpClient(handler));
    }
}
