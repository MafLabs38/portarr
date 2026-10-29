using System.Runtime.CompilerServices;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Integration.Test;

/// <summary>An in-memory connector: tests decide which workloads each environment has.</summary>
public sealed class FakeWorkloadSource : IWorkloadSource
{
    public Dictionary<string, List<Workload>> Workloads { get; } = [];

    public HashSet<string> Unreachable { get; } = [];

    /// <summary>Thrown by usage and logs, to simulate a failing connector.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Log streams stay open after their lines, like a running container with follow.</summary>
    public bool KeepLogsOpen { get; set; }

    /// <summary>The tail the last log stream was opened with.</summary>
    public int? LastTail { get; private set; }

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
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult<WorkloadUsage?>(new WorkloadUsage(0.25, 100_000_000, 500_000_000));
    }

    public Task<ILogStream?> OpenLogsAsync(CatalogEnvironment environment, string workloadName, int tail, bool follow, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        LastTail = tail;
        return Task.FromResult<ILogStream?>(new FakeLogStream(
        [
            new LogLine(DateTimeOffset.UnixEpoch, LogStream.Stdout, $"{workloadName} started"),
            new LogLine(DateTimeOffset.UnixEpoch, LogStream.Stderr, "warning: disk almost full"),
        ],
        KeepLogsOpen));
    }

    public static Workload Container(string environmentId, string name, WorkloadState state = WorkloadState.Running, WorkloadHealth health = WorkloadHealth.None)
    {
        return new Workload(environmentId, $"id-{name}", name, $"{name}:latest", state, health, state.ToString(), DateTimeOffset.UnixEpoch, null, new Dictionary<string, string>());
    }

    private sealed class FakeLogStream(IReadOnlyList<LogLine> lines, bool keepOpen) : ILogStream
    {
        public async IAsyncEnumerable<LogLine> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var line in lines)
            {
                await Task.Yield();
                yield return line;
            }

            if (keepOpen)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
