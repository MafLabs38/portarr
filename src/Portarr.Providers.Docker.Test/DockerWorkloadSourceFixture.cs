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
    public async Task should_read_the_tail_then_follow_new_lines_within_two_seconds()
    {
        await using var logs = await Subject.OpenLogsAsync(_host.Direct, _host.TalkerName, tail: 100, follow: true, TestContext.Current.CancellationToken);
        var lines = new List<LogLine>();
        var started = Stopwatch.StartNew();

        await foreach (var line in logs!.ReadAsync(TestContext.Current.CancellationToken))
        {
            lines.Add(line);
            if (line.Text.StartsWith("tick", StringComparison.Ordinal) && line.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-1))
            {
                break;
            }
        }

        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
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
}
