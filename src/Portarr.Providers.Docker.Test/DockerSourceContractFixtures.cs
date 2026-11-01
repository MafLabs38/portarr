using Microsoft.Extensions.Configuration;
using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Docker.Test;

/// <summary>The Docker workload connector against the workload contract, on a real engine (D-31, D-32).</summary>
[Trait("Category", "Integration")]
public sealed class DockerWorkloadSourceContractFixture : WorkloadSourceContract, IClassFixture<DockerHost>, IDisposable
{
    private readonly DockerHost _host;
    private readonly DockerHttpClientFactory _clients = new(new ConfigurationBuilder().Build());

    public DockerWorkloadSourceContractFixture(DockerHost host)
    {
        _host = host;
    }

    public void Dispose() => _clients.Dispose();

    protected override WorkloadSourceHarness Arrange()
    {
        return new WorkloadSourceHarness(new DockerWorkloadSource(_clients), _host.Direct, new CatalogEnvironment("server-1", "Server 1"), _host.TalkerName);
    }
}

/// <summary>The Docker infrastructure connector against the infrastructure contract, on a real engine (D-54).</summary>
[Trait("Category", "Integration")]
public sealed class DockerInfrastructureSourceContractFixture : InfrastructureSourceContract, IClassFixture<DockerHost>, IDisposable
{
    private readonly DockerHost _host;
    private readonly DockerHttpClientFactory _clients = new(new ConfigurationBuilder().Build());

    public DockerInfrastructureSourceContractFixture(DockerHost host)
    {
        _host = host;
    }

    public void Dispose() => _clients.Dispose();

    protected override InfrastructureSourceHarness Arrange()
    {
        return new InfrastructureSourceHarness(new DockerInfrastructureSource(_clients, TimeProvider.System), _host.Direct, new CatalogEnvironment("server-1", "Server 1"), []);
    }
}
