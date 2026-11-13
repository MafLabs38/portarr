using System.Collections.Concurrent;
using System.Net.Http.Json;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure.Gpu;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker;

/// <summary>
/// Which containers have a graphics card attached (D-120): the ones created with the NVIDIA runtime or a GPU device request
/// (<c>--gpus</c>), as <c>docker inspect</c> says it. How a container was created never changes, so each is inspected once, a few
/// per pass, and the answer is kept as long as the container exists. It says nothing of the card's load per container.
/// </summary>
public sealed class DockerGpuAttachmentSource : IGpuAttachmentSource
{
    /// <summary>Containers inspected per pass: a first start spreads its inspections over a few passes, never a burst.</summary>
    public const int InspectionsPerPass = 20;

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
        foreach (var id in ids.Where(id => !_known.ContainsKey((environment.Id, id))).Order(StringComparer.Ordinal).Take(InspectionsPerPass))
        {
            _known[(environment.Id, id)] = await HasGpuAsync(client, id, cancellationToken);
        }

        return ids.Where(id => _known.GetValueOrDefault((environment.Id, id))).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Whether one container was created with a GPU; a container that cannot be inspected right now is read again next pass.</summary>
    private static async Task<bool> HasGpuAsync(HttpClient client, string id, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await client.GetAsync($"containers/{Uri.EscapeDataString(id)}/json", timeout.Token);
        response.EnsureSuccessStatusCode();
        var inspect = await response.Content.ReadFromJsonAsync(DockerJsonContext.Default.ContainerInspect, timeout.Token);
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
