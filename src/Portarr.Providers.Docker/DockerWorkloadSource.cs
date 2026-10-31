using System.Net;
using System.Net.Http.Json;
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

    public const string TokenMissing = "token-missing";

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.Docker is not null;
    }

    /// <summary>A reason code when the endpoint asks for a token that the environment does not have (the Portarr reader does, D-100).</summary>
    public string? GetConfigurationError(CatalogEnvironment environment)
    {
        return environment.Docker is { TokenVariable: not null } endpoint && _clients.TokenOf(endpoint) is null ? TokenMissing : null;
    }

    public async Task<IReadOnlyList<Workload>> ListAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        using var timeout = Timeout(TimeSpan.FromSeconds(10), cancellationToken);
        var containers = await Client(environment).GetFromJsonAsync("containers/json?all=true", DockerJsonContext.Default.ListContainerSummary, timeout.Token);

        return (containers ?? []).Select(container => DockerMapper.ToWorkload(environment.Id, container)).ToList();
    }

    public async Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, string workloadName, CancellationToken cancellationToken)
    {
        // stream=false waits for two samples (about a second) so the CPU delta is meaningful.
        using var timeout = Timeout(TimeSpan.FromSeconds(10), cancellationToken);
        using var response = await Client(environment).GetAsync($"containers/{ValidName(workloadName)}/stats?stream=false", timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var stats = await response.Content.ReadFromJsonAsync(DockerJsonContext.Default.ContainerStats, timeout.Token);

        return stats is null ? null : DockerMapper.ToUsage(stats);
    }

    public async Task<ILogStream?> OpenLogsAsync(CatalogEnvironment environment, string workloadName, int tail, bool follow, CancellationToken cancellationToken)
    {
        var name = ValidName(workloadName);
        var client = Client(environment);

        // Inspect and the response headers must come quickly; only the body may stay open.
        using var opening = Timeout(TimeSpan.FromSeconds(10), cancellationToken);
        using var inspectResponse = await client.GetAsync($"containers/{name}/json", opening.Token);
        if (inspectResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        inspectResponse.EnsureSuccessStatusCode();
        var inspect = await inspectResponse.Content.ReadFromJsonAsync(DockerJsonContext.Default.ContainerInspect, opening.Token)
            ?? throw new InvalidOperationException($"Empty inspect response for {name}.");

        var query = $"containers/{name}/logs?stdout=true&stderr=true&timestamps=true&tail={Math.Clamp(tail, 0, 5000)}&follow={(follow ? "true" : "false")}";
        var response = await client.GetAsync(query, HttpCompletionOption.ResponseHeadersRead, opening.Token);
        try
        {
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new DockerLogStream(response, stream, inspect.Config.Tty);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private sealed class DockerLogStream : ILogStream
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _stream;
        private readonly bool _tty;

        public DockerLogStream(HttpResponseMessage response, Stream stream, bool tty)
        {
            _response = response;
            _stream = stream;
            _tty = tty;
        }

        public IAsyncEnumerable<LogLine> ReadAsync(CancellationToken cancellationToken)
        {
            return DockerLogReader.ReadAsync(_stream, _tty, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync();
            _response.Dispose();
        }
    }

    private static CancellationTokenSource Timeout(TimeSpan timeout, CancellationToken cancellationToken)
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
