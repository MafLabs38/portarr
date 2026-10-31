using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Test.Common.Contracts;

namespace Portarr.Providers.Kubernetes.Test;

/// <summary>node_exporter read through the Kubernetes API server's pod proxy passes the host metrics contract (CLAUDE.md rule 8).</summary>
public sealed class KubernetesPodProxyHostMetricsContractFixture : HostMetricsSourceContract
{
    private const string PodsPathStart = "/api/v1/namespaces/monitoring/pods?";
    private const string ProxyPath = "/api/v1/namespaces/monitoring/pods/ne-a:9100/proxy/metrics";

    protected override HostMetricsHarness Arrange()
    {
        var text = string.Empty;
        var cluster = new FakeCluster(path => path switch
        {
            _ when path.StartsWith(PodsPathStart, StringComparison.Ordinal) => """{ "items": [{ "metadata": { "name": "ne-a" } }] }""",
            ProxyPath => text,
            _ => null,
        });
        var environment = FakeCluster.Environment(new KubernetesNodeExporter(9100, NodeExporterAccess.ApiServer));
        var source = new KubernetesNodeExporterSource(cluster.Client, new FakeTimeProvider());

        return new HostMetricsHarness(
            source,
            new HostMetricsTarget(environment, "worker", null, 9100),
            new HostMetricsTarget(environment, "worker", new Uri("http://10.0.0.11:9100/metrics")),
            served => text = served,
            fault => cluster.Fault = fault,
            () => cluster.Requests);
    }
}
