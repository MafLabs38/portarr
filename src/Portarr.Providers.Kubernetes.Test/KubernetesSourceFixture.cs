using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Kubernetes.Test;

public sealed class KubernetesSourceFixture : IDisposable
{
    private static readonly CatalogEnvironment Environment = new("cluster", "Cluster", null, Kubernetes: new KubernetesCluster(new Uri("https://10.0.0.10:6443"), "CLUSTER_TOKEN", null));

    private readonly List<HttpRequestMessage> _requests = [];
    private readonly Dictionary<string, string> _responses = [];
    private readonly Dictionary<string, string?> _configuration = new() { ["CLUSTER_TOKEN"] = "test-token" };
    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-k8s-").FullName;

    [Fact]
    public async Task should_list_the_pods_of_every_namespace_across_pages_with_a_bearer_token_and_get_only()
    {
        _responses["/api/v1/pods?limit=500"] = """
            { "metadata": { "continue": "next/page" }, "items": [{ "metadata": { "name": "api-1", "namespace": "shop" }, "status": { "phase": "Running" } }] }
            """;
        _responses["/api/v1/pods?limit=500&continue=next%2Fpage"] = """
            { "metadata": {}, "items": [{ "metadata": { "name": "db-0", "namespace": "shop" }, "status": { "phase": "Pending" } }] }
            """;

        var workloads = await Workloads().ListAsync(Environment, TestContext.Current.CancellationToken);

        workloads.Select(w => w.Id).Should().Equal("shop/api-1", "shop/db-0");
        _requests.Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Get);
            r.Headers.Authorization!.ToString().Should().Be("Bearer test-token");
        });
    }

    [Fact]
    public async Task should_read_a_pod_usage_by_namespace_and_name_and_serve_no_logs()
    {
        _responses["/apis/metrics.k8s.io/v1beta1/namespaces/shop/pods/api-1"] = """
            { "metadata": { "name": "api-1", "namespace": "shop" }, "containers": [{ "name": "api", "usage": { "cpu": "250m", "memory": "64Mi" } }] }
            """;
        var source = (IWorkloadSource)Workloads();
        var pod = new Workload("cluster", "shop/api-1", "api-1", "api", WorkloadState.Running, WorkloadHealth.Healthy, "Running", DateTimeOffset.UnixEpoch, null, new Dictionary<string, string>(), null, "shop", "node-1");

        var usage = await source.GetUsageAsync(Environment, pod, TestContext.Current.CancellationToken);

        usage.Should().Be(new WorkloadUsage(0.25, 67_108_864, null));
        (await source.GetUsageAsync(Environment, pod with { Name = "gone" }, TestContext.Current.CancellationToken)).Should().BeNull();
        source.SupportsUsage.Should().BeTrue();
        source.SupportsLogs.Should().BeFalse();
        await FluentActions.Awaiting(() => source.GetUsageAsync(Environment, pod with { Name = "../secrets" }, TestContext.Current.CancellationToken)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_sample_nodes_as_machines_with_node_and_pod_usage()
    {
        _responses["/api/v1/nodes"] = """
            { "items": [
              { "metadata": { "name": "worker" }, "status": { "capacity": { "cpu": "4", "memory": "8Gi" } } },
              { "metadata": { "name": "control" }, "status": { "capacity": { "cpu": "4", "memory": "16Gi" } } } ] }
            """;
        _responses["/apis/metrics.k8s.io/v1beta1/nodes"] = """{ "items": [{ "metadata": { "name": "control" }, "timestamp": "2026-01-01T12:00:05Z", "window": "20.064s", "usage": { "cpu": "500m", "memory": "2Gi" } }] }""";
        _responses["/apis/metrics.k8s.io/v1beta1/pods"] = """
            { "items": [{ "metadata": { "name": "api-1", "namespace": "shop" }, "containers": [{ "name": "api", "usage": { "cpu": "100m", "memory": "1Mi" } }] }] }
            """;

        var sample = await Infrastructure().SampleAsync(Environment, [], TestContext.Current.CancellationToken);

        sample.Machines.Select(m => m.Name).Should().Equal("control", "worker");
        sample.Machines[0].CpuCores.Should().Be(0.5);
        sample.Machines[0].Scope.Should().Be(UsageScope.Host);
        sample.Machines[0].MeasuredAt.Should().Be(new DateTimeOffset(2026, 1, 1, 12, 0, 5, TimeSpan.Zero), "a point is added only when metrics-server measured again");
        sample.Resolution.Should().Be(TimeSpan.FromSeconds(20.064));
        sample.Machines[1].CpuCores.Should().BeNull("the worker has no metrics yet");
        sample.Workloads.Should().Equal(new WorkloadUsageSample("shop/api-1", 0.1, 1_048_576));
    }

    [Fact]
    public async Task should_still_list_the_nodes_without_metrics_server()
    {
        _responses["/api/v1/nodes"] = """{ "items": [{ "metadata": { "name": "control" }, "status": { "capacity": { "cpu": "2" } } }] }""";

        var sample = await Infrastructure().SampleAsync(Environment, [], TestContext.Current.CancellationToken);

        sample.Machines.Should().ContainSingle().Which.CpuCapacityCores.Should().Be(2);
        sample.Workloads.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_with_the_http_status_when_the_token_is_refused()
    {
        var source = new KubernetesWorkloadSource(Client(HttpStatusCode.Forbidden));

        var failure = await FluentActions.Awaiting(() => source.ListAsync(Environment, TestContext.Current.CancellationToken)).Should().ThrowAsync<HttpRequestException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void should_report_a_missing_token_or_ca_as_not_configured()
    {
        Workloads().GetConfigurationError(Environment).Should().BeNull();

        var withCa = Environment with { Kubernetes = Environment.Kubernetes! with { CaFile = "cluster-ca.crt" } };
        Workloads().GetConfigurationError(withCa).Should().Be(KubernetesApiClient.CaMissing);

        File.WriteAllText(Path.Combine(_folder, "cluster-ca.crt"), "not a certificate");
        Workloads().GetConfigurationError(withCa).Should().Be(KubernetesApiClient.CaInvalid);

        _configuration.Clear();
        Workloads().GetConfigurationError(Environment).Should().Be(KubernetesApiClient.TokenMissing);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private KubernetesWorkloadSource Workloads() => new(Client());

    private KubernetesInfrastructureSource Infrastructure() => new(Client());

    private KubernetesApiClient Client(HttpStatusCode? status = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build();
        var options = Options.Create(new PortarrOptions { CatalogFile = Path.Combine(_folder, "catalog.yaml") });
        return new KubernetesApiClient(configuration, options, (_, _) => new Handler(this, status));
    }

    private sealed class Handler(KubernetesSourceFixture fixture, HttpStatusCode? status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            fixture._requests.Add(request);
            if (status is { } forced)
            {
                return Task.FromResult(new HttpResponseMessage(forced));
            }

            return Task.FromResult(fixture._responses.TryGetValue(request.RequestUri!.PathAndQuery, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
