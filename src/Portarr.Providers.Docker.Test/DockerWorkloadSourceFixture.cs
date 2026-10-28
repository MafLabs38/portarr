using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Providers.Docker.Test;

/// <summary>Against the real Docker Engine of the machine running the tests (Unix socket).</summary>
[Trait("Category", "Integration")]
public sealed class DockerWorkloadSourceFixture : IAsyncLifetime
{
    private static readonly CatalogEnvironment Environment = new("local", "Local", new DockerEndpoint(new Uri("unix:///var/run/docker.sock")));

    private readonly string _name = $"portarr-test-{Guid.NewGuid():N}"[..24];
    private readonly DockerHttpClientFactory _clients = new();
    private IContainer _container = null!;

    private DockerWorkloadSource Subject => new(_clients);

    public async ValueTask InitializeAsync()
    {
        _container = new ContainerBuilder("alpine:3.20")
            .WithName(_name)
            .WithLabel("com.docker.compose.project", "portarr-test")
            .WithCommand("sh", "-c", "echo hello from stdout; echo oops from stderr >&2; sleep 300")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("oops from stderr"))
            .Build();
        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        _clients.Dispose();
    }

    [Fact]
    public async Task should_list_the_running_container_with_its_compose_project()
    {
        var workloads = await Subject.ListAsync(Environment, TestContext.Current.CancellationToken);

        var workload = workloads.Should().ContainSingle(w => w.Name == _name).Which;
        workload.State.Should().Be(WorkloadState.Running);
        workload.Image.Should().Be("alpine:3.20");
        workload.ComposeProject.Should().Be("portarr-test");
        workload.EnvironmentId.Should().Be("local");
    }

    [Fact]
    public async Task should_read_stdout_and_stderr_with_timestamps()
    {
        var lines = new List<LogLine>();
        await foreach (var line in Subject.StreamLogsAsync(Environment, _name, tail: 10, follow: false, TestContext.Current.CancellationToken))
        {
            lines.Add(line);
        }

        lines.Should().Contain(l => l.Stream == LogStream.Stdout && l.Text == "hello from stdout");
        lines.Should().Contain(l => l.Stream == LogStream.Stderr && l.Text == "oops from stderr");
        lines.Should().OnlyContain(l => l.Timestamp != null);
    }

    [Fact]
    public async Task should_report_memory_usage()
    {
        var usage = await Subject.GetUsageAsync(Environment, _name, TestContext.Current.CancellationToken);

        usage.Should().NotBeNull();
        usage!.MemoryBytes.Should().BePositive();
        usage.CpuCores.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task should_return_no_usage_for_an_unknown_container()
    {
        var usage = await Subject.GetUsageAsync(Environment, "portarr-no-such-container", TestContext.Current.CancellationToken);

        usage.Should().BeNull();
    }

    [Fact]
    public async Task should_refuse_a_name_that_is_not_a_container_name()
    {
        var read = () => Subject.GetUsageAsync(Environment, "../../info", TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<ArgumentException>();
    }
}
