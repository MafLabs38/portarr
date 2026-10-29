using System.Runtime.CompilerServices;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Integration.Test;

/// <summary>An in-memory connector: tests decide which workloads each environment has.</summary>
public sealed class FakeWorkloadSource : IWorkloadSource
{
    public Dictionary<string, List<Workload>> Workloads { get; } = [];

    public HashSet<string> Unreachable { get; } = [];

    public bool Supports(CatalogEnvironment environment) => environment.Docker is not null;

    public Task<IReadOnlyList<Workload>> ListAsync(CatalogEnvironment environment, CancellationToken cancellationToken)
    {
        if (Unreachable.Contains(environment.Id))
        {
            throw new HttpRequestException("Connection refused");
        }

        return Task.FromResult<IReadOnlyList<Workload>>(Workloads.GetValueOrDefault(environment.Id) ?? []);
    }

    public Task<WorkloadUsage?> GetUsageAsync(CatalogEnvironment environment, string workloadName, CancellationToken cancellationToken)
    {
        return Task.FromResult<WorkloadUsage?>(new WorkloadUsage(0.25, 100_000_000, 500_000_000));
    }

    public Task<ILogStream?> OpenLogsAsync(CatalogEnvironment environment, string workloadName, int tail, bool follow, CancellationToken cancellationToken)
    {
        return Task.FromResult<ILogStream?>(new FakeLogStream(
        [
            new LogLine(DateTimeOffset.UnixEpoch, LogStream.Stdout, $"{workloadName} started"),
            new LogLine(DateTimeOffset.UnixEpoch, LogStream.Stderr, "warning: disk almost full"),
        ]));
    }

    public static Workload Container(string environmentId, string name, WorkloadState state = WorkloadState.Running, WorkloadHealth health = WorkloadHealth.None)
    {
        return new Workload(environmentId, $"id-{name}", name, $"{name}:latest", state, health, state.ToString(), DateTimeOffset.UnixEpoch, null, new Dictionary<string, string>());
    }

    private sealed class FakeLogStream(IReadOnlyList<LogLine> lines) : ILogStream
    {
        public async IAsyncEnumerable<LogLine> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var line in lines)
            {
                await Task.Yield();
                yield return line;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
