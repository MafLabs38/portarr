using Microsoft.Extensions.Configuration;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;
using Portarr.Providers.Docker;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// The promise of ADR 0011: Portarr's own Docker connector reads the reader unchanged. This runs the real workload and infrastructure
/// sources against the real reader (the Rust binary, <c>PORTARR_READER_BIN</c>) and a fake Docker that records the exact requests, so a Docker call the connector learns to make
/// (or makes with another parameter) and the reader does not pass on fails here, not on a machine in production.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReaderDockerContractFixture : IAsyncLifetime
{
    private const string Token = "reader-contract-token-0123456789abcdef-01234";
    private const string ContainerId = "abc123def456abc123def456abc123def456abc123def456abc123def456abcd";

    private const string Stats = """{"cpu_stats":{"cpu_usage":{"total_usage":2000000000},"system_cpu_usage":20000000000,"online_cpus":2},"precpu_stats":{"cpu_usage":{"total_usage":1000000000},"system_cpu_usage":10000000000,"online_cpus":2},"memory_stats":{"usage":104857600,"limit":1073741824,"stats":{"inactive_file":4857600}}}""";

    private readonly ToolServer _docker = new();
    private ReaderProcess? _reader;
    private DockerHttpClientFactory _clients = null!;
    private CatalogEnvironment _environment = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (!ReaderProcess.Available)
        {
            return;
        }

        var list = $$"""[{"Id":"{{ContainerId}}","Names":["/web"],"Image":"nginx:1","State":"running","Status":"Up 3 hours","Created":1700000000,"Labels":{"com.docker.compose.project":"shop"},"Command":"nginx -g secret-arg"}]""";
        _docker
            .Serve("GET", "/info", """{"NCPU":8,"MemTotal":16777216000}""")
            .Serve("GET", "/containers/json", list)
            .Serve("GET", $"/containers/{ContainerId}/stats", Stats)
            .Serve("GET", "/containers/web/stats", Stats)
            .Serve("GET", "/containers/web/json", """{"Id":"abc","Config":{"Tty":false,"Env":["PASSWORD=hunter2"]}}""")
            .Serve("GET", "/containers/web/logs", "frame", contentType: "application/octet-stream");

        _reader = await ReaderProcess.StartAsync(Token, _docker.Url, Path.GetTempPath(), Cancel);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PORTARR_READER_CONTRACT"] = Token }).Build();
        _clients = new DockerHttpClientFactory(configuration);
        _environment = new CatalogEnvironment("server-1", "Server 1", Connectors: new Dictionary<string, object> { ["docker"] = new DockerEndpoint(new Uri(_reader.BaseUrl, "/v1/docker/"), "PORTARR_READER_CONTRACT") }, ConnectorRuntime: RuntimeNames.Docker);
    }

    public async ValueTask DisposeAsync()
    {
        if (_reader is not null)
        {
            _clients.Dispose();
            await _reader.DisposeAsync();
        }

        _docker.Dispose();
    }

    [Fact(Skip = "PORTARR_READER_BIN does not name the reader binary", SkipUnless = nameof(ReaderProcess.Available), SkipType = typeof(ReaderProcess))]
    public async Task should_let_the_workload_source_list_read_usage_and_follow_logs_with_the_requests_it_makes_to_a_socket_proxy()
    {
        var source = new DockerWorkloadSource(_clients);

        var workloads = await source.ListAsync(_environment, Cancel);
        var usage = await source.GetUsageAsync(_environment, "web", Cancel);
        await using var logs = await source.OpenLogsAsync(_environment, "web", tail: 10, follow: true, Cancel);

        workloads.Should().ContainSingle().Which.Should().Match<Workload>(w => w.Name == "web" && w.State == WorkloadState.Running && w.ComposeProject == "shop");
        usage!.MemoryBytes.Should().Be(100_000_000);
        logs.Should().NotBeNull();
        _docker.Urls.Should().Contain(u => u.EndsWith("/containers/json?all=true", StringComparison.Ordinal));
        _docker.Urls.Should().Contain(u => u.EndsWith("/containers/web/stats?stream=false", StringComparison.Ordinal));
        _docker.Urls.Should().Contain(u => u.Contains("/containers/web/logs?stdout=true&stderr=true&timestamps=true&tail=10&follow=true", StringComparison.Ordinal));
    }

    [Fact(Skip = "PORTARR_READER_BIN does not name the reader binary", SkipUnless = nameof(ReaderProcess.Available), SkipType = typeof(ReaderProcess))]
    public async Task should_let_the_infrastructure_source_sample_with_the_one_shot_request_it_makes_to_a_socket_proxy()
    {
        var workloads = await new DockerWorkloadSource(_clients).ListAsync(_environment, Cancel);
        var infrastructure = new DockerInfrastructureSource(_clients, TimeProvider.System);

        var sample = await infrastructure.SampleAsync(_environment, workloads, Cancel);

        sample.Machines.Should().ContainSingle().Which.CpuCapacityCores.Should().Be(8, "the capacity comes from /info");
        sample.Workloads.Should().ContainSingle().Which.MemoryBytes.Should().Be(100_000_000);
        _docker.Urls.Should().Contain(u => u.EndsWith($"/containers/{ContainerId}/stats?stream=false&one-shot=true", StringComparison.Ordinal), "the connector asks for Docker's one-shot sample, and the reader must pass it on");
        _docker.Calls.Should().OnlyContain(c => c.StartsWith("GET ", StringComparison.Ordinal));
    }
}
