using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker;

/// <summary>
/// Reads containers, their usage and their logs from the Docker Engine API. Only GET requests:
/// it works behind a docker-socket-proxy that allows <c>CONTAINERS=1</c> and denies writes.
/// </summary>
public sealed partial class DockerWorkloadSource : IWorkloadSource
{
    private readonly DockerHttpClientFactory _clients;

    public DockerWorkloadSource(DockerHttpClientFactory clients)
    {
        _clients = clients;
    }

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.Docker is not null;
    }

    public async Task<IReadOnlyList<Workload>> ListAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(cancellationToken, TimeSpan.FromSeconds(10));
        var containers = await Client(environment).GetFromJsonAsync("containers/json?all=true", DockerJsonContext.Default.ListContainerSummary, timeout.Token);

        return (containers ?? []).Select(container => DockerMapper.ToWorkload(environment.Id, container)).ToList();
    }

    public async Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, string workloadName, CancellationToken cancellationToken)
    {
        // stream=false waits for two samples (about a second) so the CPU delta is meaningful.
        using var timeout = Timeout(cancellationToken, TimeSpan.FromSeconds(10));
        using var response = await Client(environment).GetAsync($"containers/{ValidName(workloadName)}/stats?stream=false", timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var stats = await response.Content.ReadFromJsonAsync(DockerJsonContext.Default.ContainerStats, timeout.Token);

        return stats is null ? null : DockerMapper.ToUsage(stats);
    }

    public async IAsyncEnumerable<LogLine> StreamLogsAsync(
        CatalogEnvironment environment,
        string workloadName,
        int tail,
        bool follow,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var name = ValidName(workloadName);
        var client = Client(environment);

        var inspect = await client.GetFromJsonAsync($"containers/{name}/json", DockerJsonContext.Default.ContainerInspect, cancellationToken)
            ?? throw new KeyNotFoundException(workloadName);

        var query = $"containers/{name}/logs?stdout=true&stderr=true&timestamps=true&tail={Math.Clamp(tail, 0, 5000)}&follow={(follow ? "true" : "false")}";
        using var response = await client.GetAsync(query, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var line in DockerLogReader.ReadAsync(stream, inspect.Config.Tty, cancellationToken))
        {
            yield return line;
        }
    }

    private static CancellationTokenSource Timeout(CancellationToken cancellationToken, TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    /// <summary>Container names go into URL paths: only Docker's own name characters are accepted.</summary>
    private static string ValidName(string name)
    {
        return ContainerNamePattern().IsMatch(name)
            ? name
            : throw new ArgumentException($"'{name}' is not a valid container name.", nameof(name));
    }

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,254}$")]
    private static partial Regex ContainerNamePattern();

    private HttpClient Client(CatalogEnvironment environment)
    {
        return _clients.Get(environment.Docker ?? throw new InvalidOperationException($"Environment {environment.Id} has no Docker endpoint."));
    }
}
