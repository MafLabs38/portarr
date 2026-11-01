using System.Net;
using Microsoft.Extensions.Configuration;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Docker.Test;

/// <summary>The Portarr reader (D-100) asks for a bearer token and serves Docker under a path: the Docker connector copes with both.</summary>
public sealed class DockerReaderTokenFixture
{
    private static readonly DockerEndpoint Reader = new(new Uri("http://10.0.0.10:8485/v1/docker/"), "PORTARR_READER_TEST");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static IConfiguration Configuration(string? token) =>
        new ConfigurationBuilder().AddInMemoryCollection(token is null ? [] : new Dictionary<string, string?> { ["PORTARR_READER_TEST"] = token }).Build();

    [Theory]
    [InlineData("http://10.0.0.10:8485/v1/docker/", "http://10.0.0.10:8485/v1/docker/containers/json?all=true")]
    [InlineData("http://10.0.0.10:8485/v1/docker", "http://10.0.0.10:8485/v1/docker/containers/json?all=true")]
    [InlineData("http://socket-proxy:2375", "http://socket-proxy:2375/containers/json?all=true")]
    public void should_resolve_a_request_under_the_endpoints_base_path(string endpoint, string expected)
    {
        using var client = DockerHttpClientFactory.Create(new Uri(endpoint));

        new Uri(client.BaseAddress!, "containers/json?all=true").Should().Be(new Uri(expected));
    }

    [Fact]
    public async Task should_send_the_token_as_a_bearer_read_at_each_request()
    {
        var current = "first-token";
        var recorder = new RecordingHandler();
        using var client = new HttpClient(new BearerTokenHandler(() => current) { InnerHandler = recorder });

        await client.GetAsync("http://reader/v1/docker/info", Token);
        current = "rotated-token";
        await client.GetAsync("http://reader/v1/docker/info", Token);

        recorder.Authorizations.Should().Equal("Bearer first-token", "Bearer rotated-token");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task should_send_no_authorization_when_the_token_is_empty(string? token)
    {
        var recorder = new RecordingHandler();
        using var client = new HttpClient(new BearerTokenHandler(() => token) { InnerHandler = recorder });

        await client.GetAsync("http://reader/v1/docker/info", Token);

        recorder.Authorizations.Should().Equal((string?)null);
    }

    [Fact]
    public void should_build_a_client_per_endpoint_and_token_variable()
    {
        using var clients = new DockerHttpClientFactory(Configuration("secret-token"));

        var reader = clients.Get(Reader);
        var proxy = clients.Get(new DockerEndpoint(new Uri("http://socket-proxy:2375")));

        clients.Get(Reader).Should().BeSameAs(reader);
        proxy.Should().NotBeSameAs(reader);
        reader.BaseAddress.Should().Be(new Uri("http://10.0.0.10:8485/v1/docker/"));
    }

    [Theory]
    [InlineData(null, "token-missing")]
    [InlineData("secret-token", null)]
    public void should_report_a_missing_token_as_not_connected_before_calling(string? token, string? expected)
    {
        using var clients = new DockerHttpClientFactory(Configuration(token));
        var environment = DockerEnvironments.With("server-1", "Server 1", Reader);

        new DockerWorkloadSource(clients).GetConfigurationError(environment).Should().Be(expected);
    }

    [Fact]
    public void should_report_no_configuration_error_for_an_endpoint_that_asks_for_no_token()
    {
        using var clients = new DockerHttpClientFactory(Configuration("secret-token"));
        var environment = DockerEnvironments.With("server-1", "Server 1", new DockerEndpoint(new Uri("http://socket-proxy:2375")));

        new DockerWorkloadSource(clients).GetConfigurationError(environment).Should().BeNull();
        clients.Get(environment.Docker()!).BaseAddress.Should().Be(new Uri("http://socket-proxy:2375/"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
