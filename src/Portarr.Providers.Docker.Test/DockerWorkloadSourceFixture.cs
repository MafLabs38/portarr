using System.Diagnostics;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker.Test;

/// <summary>Against a real Docker Engine, directly and through the read-only socket proxy.</summary>
[Trait("Category", "Integration")]
public sealed class DockerWorkloadSourceFixture : IClassFixture<DockerHost>, IDisposable
{
    private readonly DockerHost _host;
    private readonly DockerHttpClientFactory _clients = new();

    public DockerWorkloadSourceFixture(DockerHost host)
    {
        _host = host;
    }

    private DockerWorkloadSource Subject => new(_clients);

    public void Dispose() => _clients.Dispose();

    [Fact]
    public async Task should_list_containers_through_the_socket_proxy_with_their_compose_project()
    {
        var workloads = await Subject.ListAsync(_host.ThroughProxy, TestContext.Current.CancellationToken);

        var talker = workloads.Should().ContainSingle(w => w.Name == _host.TalkerName).Which;
        talker.Should().Match<Workload>(w => w.State == WorkloadState.Running && w.Image == "alpine:3.20" && w.ComposeProject == "portarr-test" && w.EnvironmentId == "proxied");
    }

    [Fact]
    public async Task should_report_a_failing_healthcheck()
    {
        var health = WorkloadHealth.None;
        var deadline = Stopwatch.StartNew();
        while (health != WorkloadHealth.Unhealthy && deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            var workloads = await Subject.ListAsync(_host.Direct, TestContext.Current.CancellationToken);
            health = workloads.Single(w => w.Name == _host.UnhealthyName).Health;
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        health.Should().Be(WorkloadHealth.Unhealthy);
    }

    [Fact]
    public async Task should_read_the_tail_then_follow_new_lines_live()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var lastTick = await LastTickAsync(timeout.Token);

        await using var logs = await Subject.OpenLogsAsync(_host.Direct, _host.TalkerName, tail: 100, follow: true, timeout.Token);
        var lines = new List<LogLine>();
        var started = Stopwatch.StartNew();

        await foreach (var line in logs!.ReadAsync(timeout.Token))
        {
            lines.Add(line);
            if (Tick(line) > lastTick)
            {
                break;
            }
        }

        // The talker writes every 0.5 s and the UI promises live lines within about 2 s; the bound leaves room for a
        // loaded runner (several test assemblies at once) while still failing on a poll or a buffered stream.
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        lines.Should().Contain(l => l.Stream == LogStream.Stdout && l.Text == "hello from stdout");
        lines.Should().Contain(l => l.Stream == LogStream.Stderr && l.Text == "oops from stderr");
    }

    [Fact]
    public async Task should_report_memory_usage_through_the_proxy()
    {
        var usage = await Subject.GetUsageAsync(_host.ThroughProxy, _host.TalkerName, TestContext.Current.CancellationToken);

        usage!.MemoryBytes.Should().BePositive();
    }

    [Fact]
    public async Task should_return_nothing_for_an_unknown_container()
    {
        var usage = await Subject.GetUsageAsync(_host.Direct, "portarr-no-such-container", TestContext.Current.CancellationToken);
        var logs = await Subject.OpenLogsAsync(_host.Direct, "portarr-no-such-container", 10, false, TestContext.Current.CancellationToken);

        (usage, logs).Should().Be((null, null));
    }

    /// <summary>The tick number of a talker line ("tick 12"), 0 for other lines.</summary>
    private static int Tick(LogLine line)
    {
        return line.Text.StartsWith("tick ", StringComparison.Ordinal) && int.TryParse(line.Text.AsSpan(5), out var tick) ? tick : 0;
    }

    /// <summary>
    /// The last tick already written: any higher tick is a live line, not part of the tail. The whole tail is read, not the
    /// last line: Docker logs each stream from its own goroutine, so "oops from stderr" can land after "tick 1" in the file,
    /// and a last line that is not a tick would make the first tick look live before the stderr line was read.
    /// </summary>
    private async Task<int> LastTickAsync(CancellationToken cancellationToken)
    {
        await using var logs = await Subject.OpenLogsAsync(_host.Direct, _host.TalkerName, tail: 100, follow: false, cancellationToken);
        var last = 0;
        await foreach (var line in logs!.ReadAsync(cancellationToken))
        {
            last = Math.Max(last, Tick(line));
        }

        return last;
    }
}
