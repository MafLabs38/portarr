using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker.Test;

public sealed class DockerMapperFixture
{
    [Theory]
    [InlineData("Up 3 hours (healthy)", WorkloadHealth.Healthy)]
    [InlineData("Up 2 minutes (unhealthy)", WorkloadHealth.Unhealthy)]
    [InlineData("Up 5 seconds (health: starting)", WorkloadHealth.Starting)]
    [InlineData("Up 3 hours", WorkloadHealth.None)]
    [InlineData("Exited (0) 4 months ago", WorkloadHealth.None)]
    public void should_read_health_from_the_status_text(string status, WorkloadHealth expected)
    {
        DockerMapper.ParseHealth(status).Should().Be(expected);
    }

    [Theory]
    [InlineData("running", WorkloadState.Running)]
    [InlineData("exited", WorkloadState.Exited)]
    [InlineData("restarting", WorkloadState.Restarting)]
    [InlineData("something-new", WorkloadState.Dead)]
    public void should_read_the_state(string state, WorkloadState expected)
    {
        DockerMapper.ParseState(state).Should().Be(expected);
    }

    [Fact]
    public void should_use_the_first_name_without_slash_and_the_compose_project()
    {
        var container = new ContainerSummary(
            "0123456789abcdef", ["/media-sonarr-1"], "lscr.io/linuxserver/sonarr:4", "running", "Up 1 hour", 1_700_000_000,
            new Dictionary<string, string> { ["com.docker.compose.project"] = "media" });

        var workload = DockerMapper.ToWorkload("server-1", container);

        workload.Name.Should().Be("media-sonarr-1");
        workload.ComposeProject.Should().Be("media");
        workload.CreatedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
    }

    [Fact]
    public void should_keep_one_published_port_per_mapping_and_skip_unpublished_ones()
    {
        var ports = DockerMapper.ToPublishedPorts(
        [
            new ContainerPort("0.0.0.0", 8096, 8096, "tcp"),
            new ContainerPort("::", 8096, 8096, "tcp"),
            new ContainerPort(null, 1900, null, "udp"),
            new ContainerPort("0.0.0.0", 80, 8080, "tcp"),
        ]);

        ports.Should().Equal(new PublishedPort(80, 8080, "tcp"), new PublishedPort(8096, 8096, "tcp"));
    }

    [Fact]
    public void should_compute_cores_from_the_cpu_delta_and_memory_without_page_cache()
    {
        // Half of the host's CPU time over the interval, on 4 CPUs: 2 cores.
        var stats = new ContainerStats(
            new CpuStats(new CpuUsage(1_500), 10_000, 4),
            new CpuStats(new CpuUsage(1_000), 9_000, 4),
            new MemoryStats(300_000_000, 1_000_000_000, new Dictionary<string, long> { ["inactive_file"] = 100_000_000 }));

        var usage = DockerMapper.ToUsage(stats);

        usage.CpuCores.Should().Be(2);
        usage.MemoryBytes.Should().Be(200_000_000);
        usage.MemoryLimitBytes.Should().Be(1_000_000_000);
    }

    [Fact]
    public void should_report_zero_cores_without_a_previous_sample()
    {
        var stats = new ContainerStats(
            new CpuStats(new CpuUsage(1_500), 10_000, 4),
            new CpuStats(new CpuUsage(0), null, null),
            new MemoryStats(null, null, null));

        DockerMapper.ToUsage(stats).CpuCores.Should().Be(0);
    }

    [Fact]
    public void should_split_the_nanosecond_timestamp_from_the_text()
    {
        var line = DockerMapper.ParseLogLine(LogStream.Stderr, "2026-10-28T15:11:52.196383829Z connection refused");

        line.Timestamp.Should().Be(new DateTimeOffset(2026, 10, 28, 15, 11, 52, TimeSpan.Zero).AddTicks(1_963_838));
        line.Text.Should().Be("connection refused");
        line.Stream.Should().Be(LogStream.Stderr);
    }

    [Fact]
    public void should_keep_the_whole_line_when_it_has_no_timestamp()
    {
        DockerMapper.ParseLogLine(LogStream.Stdout, "plain text").Should().Be(new LogLine(null, LogStream.Stdout, "plain text"));
    }
}
