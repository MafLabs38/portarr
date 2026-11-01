using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Kubernetes.Test;

/// <summary>The Kubernetes workload connector against the workload contract, with a fake API server (D-31, D-55).</summary>
public sealed class KubernetesWorkloadSourceContractFixture : WorkloadSourceContract
{
    protected override WorkloadSourceHarness Arrange()
    {
        var cluster = new FakeCluster(path => path.StartsWith("/api/v1/pods", StringComparison.Ordinal)
            ? """{ "metadata": {}, "items": [{ "metadata": { "name": "api-1", "namespace": "shop" }, "status": { "phase": "Running" } }] }"""
            : null);

        return new WorkloadSourceHarness(
            new KubernetesWorkloadSource(cluster.Client),
            FakeCluster.Environment(),
            new CatalogEnvironment("server-1", "Server 1"),
            "api-1",
            fault => cluster.Fault = fault,
            () => cluster.Requests.Select(r => r.Method).ToList());
    }
}
