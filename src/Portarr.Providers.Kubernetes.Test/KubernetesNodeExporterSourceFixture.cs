using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Configuration;
using Portarr.Core.Infrastructure;

namespace Portarr.Providers.Kubernetes.Test;

/// <summary>A node's node_exporter through the API server's pod proxy (D-72): pod lookup, cache and refresh, URL, 403.</summary>
public sealed class KubernetesNodeExporterSourceFixture
{
    private const string PodsPath = "/api/v1/namespaces/monitoring/pods?labelSelector=app.kubernetes.io%2Fname%3Dnode-exporter&fieldSelector=spec.nodeName%3Dworker%2Cstatus.phase%3DRunning";

    private const string Metrics = """
        node_cpu_seconds_total{cpu="0",mode="idle"} 900
        node_cpu_seconds_total{cpu="0",mode="user"} 100
        node_memory_MemTotal_bytes 16000
        node_memory_MemAvailable_bytes 10000
        node_memory_MemFree_bytes 4000
        """;

    private static readonly CatalogEnvironment Environment = new("cluster", "Cluster", null, Kubernetes: new KubernetesCluster(
        new Uri("https://10.0.0.10:6443"),
        "CLUSTER_TOKEN",
        null,
        NodeExporter: new KubernetesNodeExporter(9100, NodeExporterAccess.ApiServer)));

    private static readonly HostMetricsTarget Target = new(Environment, "worker", null, 9100);

    private readonly List<string> _requests = [];
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = [];
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero));

    // Never read nor written: no environment of these tests names a file next to the catalog. The folder does not exist, so a
    // future test that makes the client read one fails loudly.
    private static readonly string CatalogFile = Path.Combine(Path.GetPathRoot(System.Environment.CurrentDirectory)!, "nonexistent", "catalog.yaml");

    [Fact]
    public async Task should_find_the_running_pod_of_the_node_and_read_it_through_the_pod_proxy()
    {
        Pods("ne-b", "ne-a");
        Proxy("ne-a", HttpStatusCode.OK, Metrics);

        var reading = await Source().ReadAsync(Target, TestContext.Current.CancellationToken);

        reading.Memory.TotalBytes.Should().Be(16_000);
        _requests.Should().Equal(PodsPath, "/api/v1/namespaces/monitoring/pods/ne-a:9100/proxy/metrics");
    }

    [Fact]
    public void should_build_the_lookup_and_proxy_paths_escaped_from_the_catalog_values()
    {
        KubernetesNodeExporterSource.PodsPath("ops", "app in (a,b)", "node/1").Should().Be(
            "api/v1/namespaces/ops/pods?labelSelector=app%20in%20%28a%2Cb%29&fieldSelector=spec.nodeName%3Dnode%2F1%2Cstatus.phase%3DRunning");
        KubernetesNodeExporterSource.ProxyPath("ops", "ne-a", 9101).Should().Be("api/v1/namespaces/ops/pods/ne-a:9101/proxy/metrics");
    }

    [Fact]
    public async Task should_keep_the_pod_name_between_reads_and_look_it_up_again_after_a_few_minutes()
    {
        Pods("ne-a");
        Proxy("ne-a", HttpStatusCode.OK, Metrics);
        var source = Source();

        await source.ReadAsync(Target, TestContext.Current.CancellationToken);
        await source.ReadAsync(Target, TestContext.Current.CancellationToken);
        _requests.Count(r => r == PodsPath).Should().Be(1);

        _clock.Advance(KubernetesNodeExporterSource.PodLifetime);
        await source.ReadAsync(Target, TestContext.Current.CancellationToken);
        _requests.Count(r => r == PodsPath).Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task should_look_the_pod_up_again_when_the_cached_one_is_gone(HttpStatusCode gone)
    {
        Pods("ne-old");
        Proxy("ne-old", HttpStatusCode.OK, Metrics);
        var source = Source();
        await source.ReadAsync(Target, TestContext.Current.CancellationToken);

        Pods("ne-new");
        Proxy("ne-old", gone, "");
        Proxy("ne-new", HttpStatusCode.OK, Metrics);
        var reading = await source.ReadAsync(Target, TestContext.Current.CancellationToken);

        reading.Memory.TotalBytes.Should().Be(16_000);
        _requests.TakeLast(3).Should().Equal(
            "/api/v1/namespaces/monitoring/pods/ne-old:9100/proxy/metrics",
            PodsPath,
            "/api/v1/namespaces/monitoring/pods/ne-new:9100/proxy/metrics");
    }

    [Fact]
    public async Task should_not_look_up_twice_when_a_fresh_pod_does_not_answer()
    {
        Pods("ne-a");
        Proxy("ne-a", HttpStatusCode.ServiceUnavailable, "");

        var failure = await FluentActions.Awaiting(() => Source().ReadAsync(Target, TestContext.Current.CancellationToken)).Should().ThrowAsync<HttpRequestException>();

        failure.Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _requests.Count(r => r == PodsPath).Should().Be(1);
    }

    [Fact]
    public async Task should_name_the_missing_pods_proxy_permission_on_a_403()
    {
        Pods("ne-a");
        Proxy("ne-a", HttpStatusCode.Forbidden, "");
        var source = Source();

        var failure = await FluentActions.Awaiting(() => source.ReadAsync(Target, TestContext.Current.CancellationToken)).Should().ThrowAsync<HttpRequestException>();

        failure.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        failure.Which.Message.Should().Contain("pods/proxy").And.Contain("monitoring");
        _requests.Count(r => r == PodsPath).Should().Be(1, "a refusal is not a reason to look the pod up again");
    }

    [Fact]
    public async Task should_fail_with_a_404_when_no_pod_runs_on_the_node_or_the_namespace_is_missing()
    {
        Pods();
        var source = Source();
        var failure = await FluentActions.Awaiting(() => source.ReadAsync(Target, TestContext.Current.CancellationToken)).Should().ThrowAsync<HttpRequestException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);

        _responses.Clear();
        failure = await FluentActions.Awaiting(() => source.ReadAsync(Target, TestContext.Current.CancellationToken)).Should().ThrowAsync<HttpRequestException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_use_the_namespace_and_selector_of_the_catalog_and_skip_terminating_pods()
    {
        var exporter = new KubernetesNodeExporter(9101, NodeExporterAccess.ApiServer, Namespace: "ops", Selector: "app=exporter");
        var target = Target with { Environment = Environment with { Kubernetes = Environment.Kubernetes! with { NodeExporter = exporter } }, ApiServerPort = 9101 };
        _responses["/api/v1/namespaces/ops/pods?labelSelector=app%3Dexporter&fieldSelector=spec.nodeName%3Dworker%2Cstatus.phase%3DRunning"] = (HttpStatusCode.OK, """
            { "items": [
              { "metadata": { "name": "ne-a", "deletionTimestamp": "2026-01-31T11:59:00Z" } },
              { "metadata": { "name": "ne-b" } } ] }
            """);
        _responses["/api/v1/namespaces/ops/pods/ne-b:9101/proxy/metrics"] = (HttpStatusCode.OK, Metrics);

        await Source().ReadAsync(target, TestContext.Current.CancellationToken);

        _requests.Should().HaveCount(2);
    }

    [Fact]
    public void should_support_only_the_api_server_targets_of_a_kubernetes_environment()
    {
        var source = Source();

        source.Supports(Target).Should().BeTrue();
        source.Supports(Target with { ApiServerPort = null }).Should().BeFalse();
        source.Supports(Target with { Url = new Uri("http://10.0.0.11:9100/metrics") }).Should().BeFalse();
    }

    private void Pods(params string[] names)
    {
        var items = string.Join(",", names.Select(name => $$"""{ "metadata": { "name": "{{name}}" } }"""));
        _responses[PodsPath] = (HttpStatusCode.OK, $$"""{ "items": [{{items}}] }""");
    }

    private void Proxy(string pod, HttpStatusCode status, string body)
    {
        _responses[$"/api/v1/namespaces/monitoring/pods/{pod}:9100/proxy/metrics"] = (status, body);
    }

    private KubernetesNodeExporterSource Source()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CLUSTER_TOKEN"] = "test-token" }).Build();
        var options = Options.Create(new PortarrOptions { CatalogFile = CatalogFile });
        return new KubernetesNodeExporterSource(new KubernetesApiClient(configuration, options, (_, _) => new Handler(this)), _clock);
    }

    private sealed class Handler(KubernetesNodeExporterSourceFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            fixture._requests.Add(path);
            return Task.FromResult(fixture._responses.TryGetValue(path, out var answer)
                ? new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
