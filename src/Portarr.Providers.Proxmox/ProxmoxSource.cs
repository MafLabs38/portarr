using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Proxmox;

/// <summary>
/// The LXC containers of a Proxmox node as workloads, and the node as one machine (D-128). Only GET requests, with a token that
/// should hold the <c>PVEAuditor</c> role. An LXC has no kernel of its own: like a Docker container it is a consumer of the host.
/// </summary>
public sealed class ProxmoxSource : IWorkloadSource, IInfrastructureSource
{
    /// <summary>What a Proxmox node is called on the dashboard.</summary>
    public const string MachineKind = "proxmox-node";

    /// <summary>The image shown for an LXC container: what it is.</summary>
    /// <summary>What an LXC container runs, for the Image of its workload: not an image reference (it holds a space), so the maintenance pass and the icons skip it, as they do Cloudflare's.</summary>
    public const string ContainerKind = "Proxmox LXC";

    private readonly ProxmoxClient _client;
    private readonly TimeProvider _timeProvider;

    public ProxmoxSource(ProxmoxClient client, TimeProvider timeProvider)
    {
        _client = client;
        _timeProvider = timeProvider;
    }

    public bool SupportsLogs => false;

    public TimeSpan Interval => TimeSpan.FromSeconds(5);

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.Proxmox() is not null;
    }

    public string? GetConfigurationError(CatalogEnvironment environment) => _client.GetConfigurationError(environment);

    public async Task<IReadOnlyList<Workload>> ListAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        var endpoint = Endpoint(environment);
        var guests = await _client.ListContainersAsync(endpoint, cancellationToken);
        return guests.Select(guest => ProxmoxMapper.ToWorkload(environment.Id, endpoint.Node, guest, _timeProvider.GetUtcNow())).ToList();
    }

    public async Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, string workloadName, CancellationToken cancellationToken)
    {
        var guests = await _client.ListContainersAsync(Endpoint(environment), cancellationToken);
        return guests.FirstOrDefault(g => g.Name == workloadName && g.Status == "running") is { } guest ? ProxmoxMapper.ToUsage(guest) : null;
    }

    public Task<ILogStream?> OpenLogsAsync(CatalogEnvironment environment, string workloadName, int tail, bool follow, CancellationToken cancellationToken)
    {
        return Task.FromResult<ILogStream?>(null);
    }

    public async Task<InfrastructureSample> SampleAsync(CatalogEnvironment environment, IReadOnlyList<Workload> workloads, CancellationToken cancellationToken)
    {
        var endpoint = Endpoint(environment);
        var node = await _client.GetNodeAsync(endpoint, cancellationToken);
        var guests = await _client.ListContainersAsync(endpoint, cancellationToken);

        var known = workloads.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        var usage = guests
            .Where(g => g.Status == "running" && known.Contains(g.VmId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Select(g => new WorkloadUsageSample(g.VmId.ToString(System.Globalization.CultureInfo.InvariantCulture), g.CpuCores, g.MemoryBytes))
            .ToList();
        return new InfrastructureSample([ProxmoxMapper.ToMachine(environment, node)], usage);
    }

    private static ProxmoxEndpoint Endpoint(CatalogEnvironment environment)
    {
        return environment.Proxmox() ?? throw new InvalidOperationException($"Environment {environment.Id} has no Proxmox node.");
    }
}
