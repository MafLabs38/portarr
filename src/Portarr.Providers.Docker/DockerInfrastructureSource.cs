using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Portarr.Core.Catalog;
using Portarr.Core.Infrastructure;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker;

/// <summary>
/// A Docker environment is one machine (D-54). Docker only knows its containers: the host's capacity comes
/// from <c>/info</c> (NCPU, MemTotal) and its usage is the <b>sum of its running containers</b>, flagged as such
/// (D-57); a host metrics source can replace it later. Stats are one-shot samples of the running containers,
/// a few at a time, CPU measured between two samples.
/// </summary>
public sealed partial class DockerInfrastructureSource : IInfrastructureSource
{
    /// <summary>What a Docker host is called on the dashboard.</summary>
    public const string MachineKind = "docker-host";

    /// <summary>Stats requests in flight per host: a one-shot sample answers in a few milliseconds, 4 at a time read 40 containers in about 0.1 s.</summary>
    public const int MaxConcurrency = 4;

    /// <summary>One-shot stats are cheap: every 3 s keeps the dashboard live without loading the engine.</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(3);

    /// <summary>Capacity rarely changes: /info is read again after this delay (also after a refusal).</summary>
    private static readonly TimeSpan InfoLifetime = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan StatsTimeout = TimeSpan.FromSeconds(3);

    private readonly DockerHttpClientFactory _clients;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, (DockerInfo? Info, DateTimeOffset ReadAt)> _info = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string EnvironmentId, string ContainerId), CpuCounters> _counters = new();

    public DockerInfrastructureSource(DockerHttpClientFactory clients, TimeProvider timeProvider)
    {
        _clients = clients;
        _timeProvider = timeProvider;
    }

    public TimeSpan Interval => SampleInterval;

    public bool Supports(CatalogEnvironment environment)
    {
        return environment.Docker() is not null;
    }

    public async Task<InfrastructureSample> SampleAsync(CatalogEnvironment environment, IReadOnlyList<Workload> workloads, CancellationToken cancellationToken)
    {
        var client = _clients.Get(environment.Docker() ?? throw new InvalidOperationException($"Environment {environment.Id} has no Docker endpoint."));
        var info = await GetInfoAsync(environment.Id, client, cancellationToken);

        var running = workloads.Where(w => w.State == WorkloadState.Running && ContainerIdPattern().IsMatch(w.Id)).ToList();
        using var gate = new SemaphoreSlim(MaxConcurrency);
        var samples = await Task.WhenAll(running.Select(async workload =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await SampleContainerAsync(environment.Id, client, workload.Id, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }));

        // Forget the counters of containers that stopped or were removed.
        var ids = running.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _counters.Keys.Where(k => k.EnvironmentId == environment.Id && !ids.Contains(k.ContainerId)))
        {
            _counters.TryRemove(key, out _);
        }

        var read = samples.OfType<ContainerSample>().ToList();
        var cpu = read.Where(s => s.Usage.CpuCores is not null).Select(s => s.Usage.CpuCores!.Value).ToList();

        var machine = new MachineSample(
            environment.Id,
            environment.Name,
            MachineKind,
            info?.NCpu ?? read.Select(s => (double?)s.OnlineCpus).Max(),
            info?.MemTotal ?? read.Select(s => s.MemoryLimit).Max(),
            cpu.Count > 0 ? Math.Round(cpu.Sum(), 3) : null,
            read.Count > 0 ? read.Sum(s => s.Usage.MemoryBytes) : null,
            UsageScope.Containers);

        return new InfrastructureSample([machine], read.Select(s => s.Usage).ToList());
    }

    private sealed record ContainerSample(WorkloadUsageSample Usage, int? OnlineCpus, long? MemoryLimit);

    /// <summary>One container; null when it cannot be read (it stopped in between, the engine timed out).</summary>
    private async Task<ContainerSample?> SampleContainerAsync(string environmentId, HttpClient client, string containerId, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StatsTimeout);
            using var response = await client.GetAsync($"containers/{containerId}/stats?stream=false&one-shot=true", timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var stats = await response.Content.ReadFromJsonAsync(DockerJsonContext.Default.ContainerStats, timeout.Token);
            if (stats is null)
            {
                return null;
            }

            var key = (environmentId, containerId);
            var (usage, cores, counters) = DockerMapper.ToSampledUsage(stats, _counters.GetValueOrDefault(key));
            _counters[key] = counters;

            return new ContainerSample(new WorkloadUsageSample(containerId, cores, usage.MemoryBytes), stats.CpuStats.OnlineCpus, stats.MemoryStats.Limit);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// The host's capacity, cached. A socket proxy without <c>INFO=1</c> refuses it (403): the capacity is
    /// then taken from the containers' stats (online CPUs, and the memory limit of an unlimited container,
    /// which is the host's memory).
    /// </summary>
    private async Task<DockerInfo?> GetInfoAsync(string environmentId, HttpClient client, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_info.TryGetValue(environmentId, out var cached) && now - cached.ReadAt < InfoLifetime)
        {
            return cached.Info;
        }

        DockerInfo? info = null;
        using (var response = await client.GetAsync("info", cancellationToken))
        {
            if (response.IsSuccessStatusCode)
            {
                info = await response.Content.ReadFromJsonAsync(DockerJsonContext.Default.DockerInfo, cancellationToken);
            }
            else if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.NotFound))
            {
                response.EnsureSuccessStatusCode();
            }
        }

        _info[environmentId] = (info, now);
        return info;
    }

    [GeneratedRegex("^[a-f0-9]{12,64}$")]
    private static partial Regex ContainerIdPattern();
}
