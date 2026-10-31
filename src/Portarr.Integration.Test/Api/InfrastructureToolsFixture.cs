using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;
using Portarr.Providers.Kubernetes;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// V2 over the real HTTP pipeline: the live infrastructure read from a Kubernetes API server (nodes, metrics-server,
/// the node_exporter pod proxy) and from a host's node_exporter, both played by WireMock. The formulas are
/// unit-tested (NodeExporterSamplerFixture) and both connectors have their contract suite; this proves the wiring.
/// </summary>
[Trait("Category", "Integration")]
public sealed class InfrastructureToolsFixture : IAsyncLifetime
{
    private const string Pods = """
        { "metadata": {}, "items": [
          { "metadata": { "name": "api-1", "namespace": "shop" }, "spec": { "nodeName": "worker" }, "status": { "phase": "Running" } } ] }
        """;

    private readonly TestDatabase _database;
    private readonly ToolServer _host = new();
    private readonly ToolServer _docker = new();
    private readonly ToolServer _cluster = new();
    private PortarrFactory _factory = null!;
    private HttpClient _client = null!;

    public InfrastructureToolsFixture(TestDatabase database)
    {
        _database = database;
    }

    public async ValueTask InitializeAsync()
    {
        _cluster
            .Serve("GET", "/api/v1/nodes", """{ "items": [{ "metadata": { "name": "worker" }, "status": { "capacity": { "cpu": "4", "memory": "8Gi" }, "conditions": [{ "type": "Ready", "status": "True" }] } }] }""")
            .Serve("GET", "/apis/metrics.k8s.io/v1beta1/nodes", """{ "items": [{ "metadata": { "name": "worker" }, "timestamp": "2026-01-01T12:00:05Z", "window": "20s", "usage": { "cpu": "500m", "memory": "2Gi" } }] }""")
            .Serve("GET", "/apis/metrics.k8s.io/v1beta1/pods", """{ "items": [{ "metadata": { "name": "api-1", "namespace": "shop" }, "containers": [{ "name": "api", "usage": { "cpu": "100m", "memory": "1Mi" } }] }] }""")
            .Serve("GET", "/api/v1/pods", Pods)
            .Serve("GET", "/apis/apps/v1/statefulsets", """{ "items": [] }""")
            .Serve("GET", "/api/v1/namespaces/monitoring/pods", """{ "items": [{ "metadata": { "name": "ne-a" } }] }""")
            .Serve("GET", "/api/v1/namespaces/monitoring/pods/ne-a:9100/proxy/metrics", Exposition(900, 100), contentType: "text/plain");
        _host.Serve("GET", "/metrics", Exposition(400, 100), contentType: "text/plain");
        _docker.Serve("GET", "/info", """{ "NCPU": 1, "MemTotal": 16000 }""");

        var catalog = $$"""
            environments:
              - { id: server-1, name: Server 1, docker: { url: "{{_docker.Url}}" }, hostMetrics: { nodeExporter: "{{_host.Url}}/metrics" } }
              - { id: cluster, name: Cluster, kubernetes: { server: "{{_cluster.Url}}", tokenEnv: CLUSTER_TOKEN, nodeExporter: { port: 9100, access: api-server } } }
            apps:
              - { id: shop, name: Shop, workloads: [{ runtime: kubernetes@cluster, match: { name: api-1 } }] }
            """;
        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), catalog);
        // The Docker host's containers are faked (none here); the cluster's workloads are read from the Kubernetes API.
        _factory = new PortarrFactory(
            options,
            new Dictionary<string, string?> { ["CLUSTER_TOKEN"] = "cluster-token" },
            services => services.AddSingleton<IWorkloadSource, KubernetesWorkloadSource>());
        _client = await _factory.CreateSignedInClientAsync();
        await _factory.Services.GetRequiredService<IWorkloadInventory>().RefreshAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _host.Dispose();
        _docker.Dispose();
        _cluster.Dispose();
    }

    [Fact]
    public async Task should_show_the_nodes_of_a_cluster_with_metrics_server_usage()
    {
        await _factory.Services.GetRequiredService<IInfrastructureMonitor>().SampleAsync(TestContext.Current.CancellationToken);

        var cluster = (await GetInfrastructureAsync()).Single(e => e.GetProperty("id").GetString() == "cluster");

        cluster.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        var node = cluster.GetProperty("machines").EnumerateArray().Single();
        node.GetProperty("id").GetString().Should().Be("worker");
        node.GetProperty("kind").GetString().Should().Be("kubernetes-node");
        node.GetProperty("ready").GetBoolean().Should().BeTrue();
        node.GetProperty("cpuCapacityCores").GetDouble().Should().Be(4);
        node.GetProperty("cpuCores").GetDouble().Should().Be(0.5);
        node.GetProperty("memoryBytes").GetInt64().Should().Be(2L * 1024 * 1024 * 1024);
    }

    [Fact]
    public async Task should_read_node_exporter_over_http_and_through_the_pod_proxy_and_measure_the_cpu_between_two_reads()
    {
        await _factory.Services.GetRequiredService<IInfrastructureMonitor>().SampleAsync(TestContext.Current.CancellationToken);
        var monitor = _factory.Services.GetServices<IHostedService>().OfType<HostMetricsMonitor>().Single();
        await SampleUntilBothAgentsAnsweredAsync(monitor);

        // One second later each core was busy for half of it. The agents answer the new counters before the clock moves:
        // advancing it also wakes the monitor's own 1 s loop, which must never read between the reset and the new answer.
        _host.Reset();
        _host.Serve("GET", "/metrics", Exposition(400.5, 100.5), contentType: "text/plain");
        _cluster.Serve("GET", "/api/v1/namespaces/monitoring/pods/ne-a:9100/proxy/metrics", Exposition(900.25, 100.75), contentType: "text/plain");
        _factory.Clock.Advance(TimeSpan.FromSeconds(2));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        var environments = await GetInfrastructureAsync();
        var docker = HostOf(environments, "server-1", "server-1");
        docker.GetProperty("source").GetString().Should().Be("node-exporter");
        docker.GetProperty("cores").GetInt32().Should().Be(1);
        docker.GetProperty("cpuPercent").GetDouble().Should().Be(50);
        docker.GetProperty("memoryTotalBytes").GetInt64().Should().Be(16_000);
        docker.GetProperty("memoryUsedBytes").GetInt64().Should().Be(6_000);

        var node = HostOf(environments, "cluster", "worker");
        node.GetProperty("cpuPercent").GetDouble().Should().Be(75, "the core was idle a quarter of the second");
        node.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        _cluster.Calls.Should().Contain("GET /api/v1/namespaces/monitoring/pods/ne-a:9100/proxy/metrics");
        _cluster.Calls.Should().OnlyContain(c => c.StartsWith("GET ", StringComparison.Ordinal), "Portarr never writes to the cluster");
    }

    [Fact]
    public async Task should_keep_the_last_values_and_say_why_when_the_agent_stops_answering()
    {
        await _factory.Services.GetRequiredService<IInfrastructureMonitor>().SampleAsync(TestContext.Current.CancellationToken);
        var monitor = _factory.Services.GetServices<IHostedService>().OfType<HostMetricsMonitor>().Single();
        await SampleUntilBothAgentsAnsweredAsync(monitor);

        _host.Reset();
        _host.Serve("GET", "/metrics", "forbidden", status: 403, contentType: "text/plain");
        _factory.Clock.Advance(TimeSpan.FromSeconds(2));
        await monitor.SampleAsync(TestContext.Current.CancellationToken);

        var docker = HostOf(await GetInfrastructureAsync(), "server-1", "server-1");
        docker.GetProperty("error").GetString().Should().Be("http-403");
        docker.GetProperty("memoryTotalBytes").GetInt64().Should().Be(16_000, "the last good reading stays on the card");
    }

    /// <summary>
    /// A CPU load is the difference between two scrapes, so the first one must have been read before the counters change.
    /// A read that outlasts the monitor's timeout (a cold, loaded runner compiles the parser and the HTTP stack during
    /// it) is stored as a failure and retried once the clock has passed its delay: sample again until both agents answered.
    /// </summary>
    private async Task SampleUntilBothAgentsAnsweredAsync(HostMetricsMonitor monitor)
    {
        HostMachineKey[] agents = [new("server-1", "server-1"), new("cluster", "worker")];
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await monitor.SampleAsync(TestContext.Current.CancellationToken);
            if (agents.All(agent => monitor.Current.TryGetValue(agent, out var view) && view.Latest is not null))
            {
                return;
            }

            _factory.Clock.Advance(TimeSpan.FromSeconds(2));
        }

        throw new InvalidOperationException("The node_exporter agents did not answer a first scrape.");
    }

    private static JsonElement HostOf(IEnumerable<JsonElement> environments, string environmentId, string machineId)
    {
        var machine = environments.Single(e => e.GetProperty("id").GetString() == environmentId)
            .GetProperty("machines").EnumerateArray().Single(m => m.GetProperty("id").GetString() == machineId);
        return machine.GetProperty("hostMetrics");
    }

    private async Task<List<JsonElement>> GetInfrastructureAsync()
    {
        return (await _client.GetFromJsonAsync<List<JsonElement>>("/api/v1/infrastructure", TestContext.Current.CancellationToken))!;
    }

    private static string Exposition(double idle, double busy) => $$"""
        node_cpu_seconds_total{cpu="0",mode="idle"} {{idle.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
        node_cpu_seconds_total{cpu="0",mode="user"} {{busy.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
        node_memory_MemTotal_bytes 16000
        node_memory_MemAvailable_bytes 10000
        node_memory_MemFree_bytes 4000
        """;
}
