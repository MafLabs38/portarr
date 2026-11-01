using Portarr.Core.Catalog;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Kubernetes.Test;

/// <summary>The Kubernetes infrastructure connector against the infrastructure contract, with a fake API server (D-54, D-55).</summary>
public sealed class KubernetesInfrastructureSourceContractFixture : InfrastructureSourceContract
{
    protected override InfrastructureSourceHarness Arrange()
    {
        var cluster = new FakeCluster(path => path switch
        {
            "/api/v1/nodes" => """{ "items": [{ "metadata": { "name": "worker" }, "status": { "capacity": { "cpu": "4", "memory": "8Gi" } } }] }""",
            "/apis/metrics.k8s.io/v1beta1/nodes" => """{ "items": [{ "metadata": { "name": "worker" }, "timestamp": "2026-01-01T12:00:05Z", "window": "20s", "usage": { "cpu": "500m", "memory": "2Gi" } }] }""",
            "/apis/metrics.k8s.io/v1beta1/pods" => """{ "items": [] }""",
            _ => null,
        });

        return new InfrastructureSourceHarness(
            new KubernetesInfrastructureSource(cluster.Client),
            FakeCluster.Environment(),
            new CatalogEnvironment("server-1", "Server 1"),
            [],
            fault => cluster.Fault = fault,
            () => cluster.Requests.Select(r => r.Method).ToList());
    }
}
