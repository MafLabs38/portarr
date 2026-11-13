using System.Collections.Concurrent;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure.Gpu;
using Portarr.Core.Net;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker;

/// <summary>
/// Which containers have a graphics card attached (D-120): the ones created with the NVIDIA runtime or a GPU device request
/// (<c>--gpus</c>), as <c>docker inspect</c> says it. How a container was created never changes, so each is inspected once, a few
/// per pass, a few at a time, and the answer is kept as long as the container exists. One that cannot be inspected is read again next pass. It says nothing of the card's load per container.
/// </summary>
public sealed class DockerGpuAttachmentSource : IGpuAttachmentSource
{
    /// <summary>Containers inspected per pass: a first start spreads its inspections over a few passes, never a burst.</summary>
    public const int InspectionsPerPass = 20;

    /// <summary>Inspections made at the same time: a handful, so a slow daemon holds back little.</summary>
    private const int Concurrency = 4;

    /// <summary>The largest inspect answer read: it holds the whole container configuration, labels included.</summary>
    private const int MaxInspectBytes = 512 * 1024;

    private const string NvidiaRuntime = "nvidia";

    private readonly Func<DockerEndpoint, HttpClient> _client;
    private readonly ConcurrentDictionary<(string EnvironmentId, string WorkloadId), bool> _known = new();

    public DockerGpuAttachmentSource(DockerHttpClientFactory clients)
        : this(clients.Get)
    {
    }

    internal DockerGpuAttachmentSource(Func<DockerEndpoint, HttpClient> client)
    {
        _client = client;
    }

    public bool Supports(CatalogEnvironment environment) => environment.Docker() is not null;

    public async Task<IReadOnlySet<string>> AttachedAsync(CatalogEnvironment environment, IReadOnlyList<Workload> workloads, CancellationToken cancellationToken)
    {
        var endpoint = environment.Docker() ?? throw new InvalidOperationException("The environment declares no Docker endpoint.");
        var ids = workloads.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);

        // A container that is gone is forgotten: its id never comes back.
        foreach (var key in _known.Keys.Where(k => k.EnvironmentId == environment.Id && !ids.Contains(k.WorkloadId)))
        {
            _known.TryRemove(key, out _);
        }

        var client = _client(endpoint);
        var unknown = ids.Where(id => !_known.ContainsKey((environment.Id, id))).Order(StringComparer.Ordinal).Take(InspectionsPerPass).ToList();

        // A few at a time, each on its own: a container that cannot be inspected right now is read again next pass and never fails the others.
        await Parallel.ForEachAsync(unknown, new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = cancellationToken }, async (id, token) =>
        {
            try
            {
                _known[(environment.Id, id)] = await HasGpuAsync(client, id, token);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidDataException && !cancellationToken.IsCancellationRequested)
            {
                // Not cached: the next pass asks again.
            }
        });

        return ids.Where(id => _known.GetValueOrDefault((environment.Id, id))).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Whether one container was created with a GPU; a container that cannot be inspected right now is read again next pass.</summary>
    private static async Task<bool> HasGpuAsync(HttpClient client, string id, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await client.GetAsync($"containers/{Uri.EscapeDataString(id)}/json", timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var body = new CappedStream(await response.Content.ReadAsStreamAsync(timeout.Token), MaxInspectBytes);
        var inspect = await System.Text.Json.JsonSerializer.DeserializeAsync(body, DockerJsonContext.Default.ContainerInspect, timeout.Token);
        var host = inspect?.HostConfig;
        return host is not null
            && (string.Equals(host.Runtime, NvidiaRuntime, StringComparison.OrdinalIgnoreCase)
                || (host.DeviceRequests ?? []).Any(IsGpuRequest));
    }

    private static bool IsGpuRequest(DeviceRequest request)
    {
        return string.Equals(request.Driver, NvidiaRuntime, StringComparison.OrdinalIgnoreCase)
            || (request.Capabilities ?? []).Any(set => set.Contains("gpu", StringComparer.OrdinalIgnoreCase));
    }
}
