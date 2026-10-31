using Microsoft.Extensions.Configuration;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Docker.Test;

/// <summary>The Portarr reader (D-100) asks for a bearer token and serves Docker under a path: the Docker connector copes with both.</summary>
public sealed class DockerReaderTokenFixture
{
    private static readonly DockerEndpoint Reader = new(new Uri("http://10.0.0.10:8485/v1/docker/"), "READER_TOKEN");

    private static IConfiguration Configuration(string? token) =>
        new ConfigurationBuilder().AddInMemoryCollection(token is null ? [] : new Dictionary<string, string?> { ["READER_TOKEN"] = token }).Build();

    [Fact]
    public void should_send_the_token_as_a_bearer_and_keep_the_base_path()
    {
        using var clients = new DockerHttpClientFactory(Configuration("secret-token"));

        var client = clients.Get(Reader);

        client.BaseAddress.Should().Be(new Uri("http://10.0.0.10:8485/v1/docker/"));
        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        client.DefaultRequestHeaders.Authorization.Parameter.Should().Be("secret-token");
    }

    [Fact]
    public void should_send_nothing_to_an_endpoint_that_asks_for_no_token()
    {
        using var clients = new DockerHttpClientFactory(Configuration("secret-token"));

        clients.Get(new DockerEndpoint(new Uri("http://socket-proxy:2375"))).DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "token-missing")]
    [InlineData("secret-token", null)]
    public void should_report_a_missing_token_as_not_connected_before_calling(string? token, string? expected)
    {
        using var clients = new DockerHttpClientFactory(Configuration(token));
        var environment = new CatalogEnvironment("server-1", "Server 1", Reader);

        new DockerWorkloadSource(clients).GetConfigurationError(environment).Should().Be(expected);
    }

    [Fact]
    public void should_report_no_configuration_error_for_an_endpoint_without_a_token_variable()
    {
        using var clients = new DockerHttpClientFactory();
        var environment = new CatalogEnvironment("server-1", "Server 1", new DockerEndpoint(new Uri("http://socket-proxy:2375")));

        new DockerWorkloadSource(clients).GetConfigurationError(environment).Should().BeNull();
    }
}
