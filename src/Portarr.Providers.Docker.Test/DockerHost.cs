using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Portarr.Core.Catalog;

namespace Portarr.Providers.Docker.Test;

/// <summary>
/// Containers shared by the Docker integration tests, started once per class: a chatty one, one
/// whose healthcheck always fails, and the read-only socket proxy Portarr uses in production.
/// </summary>
public sealed class DockerHost : IAsyncLifetime
{
    private readonly string _prefix = $"portarr-test-{Guid.NewGuid():N}"[..20];

    public IContainer Talker { get; private set; } = null!;

    public IContainer Unhealthy { get; private set; } = null!;

    public IContainer Proxy { get; private set; } = null!;

    /// <summary>The engine Testcontainers itself uses (honors DOCKER_HOST, rootless, Docker Desktop).</summary>
    public CatalogEnvironment Direct { get; } = DockerEnvironments.With("local", "Local", new DockerEndpoint(TestcontainersSettings.OS.DockerEndpointAuthConfig.Endpoint));

    public CatalogEnvironment ThroughProxy { get; private set; } = null!;

    public string TalkerName => $"{_prefix}-talker";

    public string UnhealthyName => $"{_prefix}-sick";

    public async ValueTask InitializeAsync()
    {
        Talker = new ContainerBuilder("alpine:3.20")
            .WithName(TalkerName)
            .WithLabel("com.docker.compose.project", "portarr-test")
            .WithCommand("sh", "-c", "echo hello from stdout; echo oops from stderr >&2; i=0; while true; do i=$((i+1)); echo tick $i; sleep 0.5; done")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("oops from stderr"))
            .Build();

        Unhealthy = new ContainerBuilder("alpine:3.20")
            .WithName(UnhealthyName)
            .WithCommand("sleep", "300")
            .WithCreateParameterModifier(parameters => parameters.Healthcheck = new HealthcheckConfig
            {
                Test = ["CMD-SHELL", "exit 1"],
                Interval = TimeSpan.FromMilliseconds(500),
                Retries = 1,
            })
            .Build();

        Proxy = new ContainerBuilder("tecnativa/docker-socket-proxy:v0.4.2")
            .WithEnvironment("CONTAINERS", "1")
            .WithEnvironment("POST", "0")
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock", AccessMode.ReadOnly)
            .WithPortBinding(2375, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(2375).ForPath("/version")))
            .Build();

        await Task.WhenAll(Talker.StartAsync(), Unhealthy.StartAsync(), Proxy.StartAsync());
        ThroughProxy = DockerEnvironments.With("proxied", "Proxied", new DockerEndpoint(new Uri($"tcp://{Proxy.Hostname}:{Proxy.GetMappedPublicPort(2375)}")));
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(Talker.DisposeAsync().AsTask(), Unhealthy.DisposeAsync().AsTask(), Proxy.DisposeAsync().AsTask());
    }
}
