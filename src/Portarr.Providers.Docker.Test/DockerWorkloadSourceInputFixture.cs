using Portarr.Core.Catalog;

namespace Portarr.Providers.Docker.Test;

/// <summary>Input checks that run before any request: no Docker Engine needed.</summary>
public sealed class DockerWorkloadSourceInputFixture : IDisposable
{
    private static readonly CatalogEnvironment Environment = new("e", "E", new DockerEndpoint(new Uri("http://127.0.0.1:1")));

    private readonly DockerHttpClientFactory _clients = new();

    public void Dispose() => _clients.Dispose();

    [Theory]
    [InlineData("../../info")]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task should_refuse_names_that_are_not_container_names_before_calling_docker(string name)
    {
        var source = new DockerWorkloadSource(_clients);

        await FluentActions.Awaiting(() => source.GetUsageAsync(Environment, name, TestContext.Current.CancellationToken)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => source.OpenLogsAsync(Environment, name, 10, false, TestContext.Current.CancellationToken)).Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("tcp://socket-proxy", "http://socket-proxy:2375/")]
    [InlineData("tcp://socket-proxy:2376", "http://socket-proxy:2376/")]
    [InlineData("unix:///var/run/docker.sock", "http://docker/")]
    public void should_target_the_endpoint_of_each_scheme(string endpoint, string expected)
    {
        using var client = DockerHttpClientFactory.Create(new Uri(endpoint));

        client.BaseAddress.Should().Be(new Uri(expected));
    }
}
