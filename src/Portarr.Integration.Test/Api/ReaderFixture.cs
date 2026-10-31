using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;
using Portarr.Providers.Docker;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// A machine connected with one line, <c>reader: { url, tokenEnv }</c> (D-100, D-101): Portarr reads the real Portarr reader (the Rust
/// binary, <c>PORTARR_READER_BIN</c>) over real HTTP, the reader reads a fake Docker and a fake <c>/proc</c>, and the containers, their usage and the live host
/// metrics show up through the existing Docker and node_exporter connectors, with the token as the only credential.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReaderFixture : IAsyncLifetime
{
    private const string Token = "reader-integration-token-0123456789abcdef-0123";

    private const string Containers = """[{"Id":"abc123","Names":["/web"],"Image":"nginx:1","State":"running","Status":"Up 3 hours","Created":1700000000,"Labels":{}}]""";

    private const string Stats = """{"cpu_stats":{"cpu_usage":{"total_usage":2000000000},"system_cpu_usage":20000000000,"online_cpus":2},"precpu_stats":{"cpu_usage":{"total_usage":1000000000},"system_cpu_usage":10000000000,"online_cpus":2},"memory_stats":{"usage":104857600,"limit":1073741824,"stats":{"inactive_file":4857600}}}""";

    private readonly TestDatabase _database;
    private readonly ToolServer _docker = new();
    private readonly string _proc = Path.Combine(Path.GetTempPath(), "portarr-reader-it-" + Guid.NewGuid().ToString("N"));
    private ReaderProcess? _reader;
    private PortarrFactory _factory = null!;
    private HttpClient _client = null!;

    public ReaderFixture(TestDatabase database)
    {
        _database = database;
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (!ReaderProcess.Available)
        {
            return;
        }

        _docker
            .Serve("GET", "/info", """{"NCPU":8,"MemTotal":16777216000}""")
            .Serve("GET", "/containers/json", Containers)
            .Serve("GET", "/containers/web/stats", Stats);
        Directory.CreateDirectory(_proc);
        WriteKernelFiles(cpu0: (150, 300), cpu1: (150, 300));

        _reader = await ReaderProcess.StartAsync(Token, _docker.Url, _proc, Cancel);

        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), $$"""
            environments:
              - { id: server-1, name: Server 1, reader: { url: "{{_reader.BaseUrl.ToString().TrimEnd('/')}}", tokenEnv: PORTARR_READER_TEST } }
            apps:
              - { id: shop, name: Shop, workloads: [{ runtime: docker@server-1, match: { name: web } }] }
            """, Cancel);
        // The factory fakes the Docker connector so API tests never need an Engine; here the real one reads the reader.
        _factory = new PortarrFactory(options, new Dictionary<string, string?> { ["PORTARR_READER_TEST"] = Token }, services =>
        {
            services.RemoveAll<IWorkloadSource>();
            services.AddSingleton<IWorkloadSource, DockerWorkloadSource>();
        });
        _client = await _factory.CreateSignedInClientAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_reader is null)
        {
            _docker.Dispose();
            return;
        }

        _client.Dispose();
        await _factory.DisposeAsync();
        await _reader.DisposeAsync();
        _docker.Dispose();
        Directory.Delete(_proc, recursive: true);
    }

    [Fact(Skip = "PORTARR_READER_BIN does not name the reader binary", SkipUnless = nameof(ReaderProcess.Available), SkipType = typeof(ReaderProcess))]
    public async Task should_show_the_containers_their_usage_and_the_live_host_metrics_of_a_machine_connected_by_its_reader()
    {
        await _factory.Services.GetRequiredService<IWorkloadInventory>().RefreshAsync(Cancel);
        await _factory.Services.GetRequiredService<IInfrastructureMonitor>().SampleAsync(Cancel);
        var monitor = _factory.Services.GetServices<IHostedService>().OfType<HostMetricsMonitor>().Single();
        await monitor.SampleAsync(Cancel);

        // One second later cpu0 was half idle and cpu1 fully idle: a quarter of the machine was busy.
        WriteKernelFiles(cpu0: (200, 350), cpu1: (150, 400));
        _factory.Clock.Advance(TimeSpan.FromSeconds(2));
        await monitor.SampleAsync(Cancel);

        var workload = await GetAsync<WorkloadResourceView>("/api/v1/workloads/server-1/web");
        var usage = await GetAsync<JsonElement>("/api/v1/workloads/server-1/web/usage");
        var environments = await GetAsync<List<JsonElement>>("/api/v1/infrastructure");

        workload!.State.Should().Be("running");
        usage.GetProperty("memoryBytes").GetInt64().Should().Be(100_000_000L, "the page cache (inactive file) is not counted");
        var machine = environments.Single(e => e.GetProperty("id").GetString() == "server-1").GetProperty("machines").EnumerateArray().Single();
        var host = machine.GetProperty("hostMetrics");
        host.GetProperty("source").GetString().Should().Be("node-exporter", "the reader speaks node_exporter's format");
        host.GetProperty("cpuPercent").GetDouble().Should().Be(25);
        host.GetProperty("cores").GetInt32().Should().Be(2);
        host.GetProperty("memoryTotalBytes").GetInt64().Should().Be(16000L * 1024);
        host.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        _docker.Calls.Should().OnlyContain(c => c.StartsWith("GET ", StringComparison.Ordinal), "the reader never writes to Docker");
    }

    /// <summary>A GET that says which URL failed and what it answered, so a failing connector is easy to find.</summary>
    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await _client.GetAsync(url, Cancel);
        var body = await response.Content.ReadAsStringAsync(Cancel);
        response.IsSuccessStatusCode.Should().BeTrue($"GET {url} answered {(int)response.StatusCode}: {body} (Docker saw: {string.Join(", ", _docker.Calls)})");
        return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private void WriteKernelFiles((long User, long Idle) cpu0, (long User, long Idle) cpu1)
    {
        File.WriteAllText(
            Path.Combine(_proc, "stat"),
            $"cpu  {cpu0.User + cpu1.User} 0 100 {cpu0.Idle + cpu1.Idle} 0 0 0 0 0 0\ncpu0 {cpu0.User} 0 50 {cpu0.Idle} 0 0 0 0 0 0\ncpu1 {cpu1.User} 0 50 {cpu1.Idle} 0 0 0 0 0 0\n");
        File.WriteAllText(
            Path.Combine(_proc, "meminfo"),
            "MemTotal:       16000 kB\nMemFree:         4000 kB\nMemAvailable:    8000 kB\nBuffers:          100 kB\nCached:          2000 kB\nSReclaimable:     300 kB\nSwapTotal:       1000 kB\nSwapFree:         900 kB\n");
        File.WriteAllText(Path.Combine(_proc, "loadavg"), "0.50 0.40 0.30 1/200 12345\n");
    }

    private sealed record WorkloadResourceView(string State);
}
