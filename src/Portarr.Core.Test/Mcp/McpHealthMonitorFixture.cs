using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Mcp;
using Portarr.Core.Test.Checks;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Mcp;

public sealed class McpHealthMonitorFixture
{
    private const string Result = """{"jsonrpc":"2.0","id":1,"result":{}}""";

    private const string Memory = """
        mcpServers:
          - id: memory
            transport: http
            http: { url: "https://memory.homelab.example/mcp" }
        """;

    private static readonly DateTimeOffset Start = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly ICatalogService _catalog = Substitute.For<ICatalogService>();
    private readonly IMcpHealthStore _store = Substitute.For<IMcpHealthStore>();
    private readonly List<string> _asked = [];
    private HttpStatusCode _status = HttpStatusCode.OK;
    private string _body = Result;

    public McpHealthMonitorFixture()
    {
        var handler = new FakeHttpHandler((request, _) =>
        {
            _asked.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
        });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(HttpCheckRunner.HttpClientName).Returns(_ => new HttpClient(handler));
        Runner = new McpCheckRunner(factory, _clock);
        Given(Memory);
    }

    private McpCheckRunner Runner { get; }

    private McpHealthMonitor Monitor => new(_catalog, _store, Runner, _clock, NullLogger<McpHealthMonitor>.Instance);

    [Fact]
    public async Task should_check_a_remote_server_and_keep_its_health()
    {
        var monitor = Monitor;

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        var health = monitor.Current["memory"];
        health.State.Should().Be(McpHealthState.Healthy);
        health.Since.Should().Be(Start);
        health.LastHealthyAt.Should().Be(Start);
        health.LastCheckedAt.Should().Be(Start);
        await _store.Received(1).SaveAsync(Arg.Is<McpServerHealth>(h => h.ServerId == "memory" && h.State == McpHealthState.Healthy), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_check_again_only_when_the_interval_has_passed()
    {
        var monitor = Monitor;

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(4));
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        _asked.Should().HaveCount(1);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        _asked.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_say_since_when_a_server_is_down_and_when_it_was_last_healthy()
    {
        var monitor = Monitor;
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        _status = HttpStatusCode.BadGateway;
        _body = "Bad gateway";
        _clock.Advance(TimeSpan.FromMinutes(5));
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        var downSince = _clock.GetUtcNow();
        _clock.Advance(TimeSpan.FromDays(12));
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        var health = monitor.Current["memory"];
        health.State.Should().Be(McpHealthState.Down);
        health.Error.Should().Be("http-502");
        health.Since.Should().Be(downSince);
        health.LastHealthyAt.Should().Be(Start);
        health.LastCheckedAt.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task should_start_a_new_healthy_period_when_a_down_server_answers_again()
    {
        var monitor = Monitor;
        _status = HttpStatusCode.BadGateway;
        _body = "Bad gateway";
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(5));

        _status = HttpStatusCode.OK;
        _body = Result;
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        var health = monitor.Current["memory"];
        health.State.Should().Be(McpHealthState.Healthy);
        health.Since.Should().Be(_clock.GetUtcNow());
        health.LastHealthyAt.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task should_not_check_a_local_server_nor_one_whose_check_is_off()
    {
        Given("""
            mcpServers:
              - { id: local, transport: stdio, stdio: { command: npx } }
              - { id: quiet, transport: http, http: { url: "https://quiet.homelab.example/mcp" }, check: false }
            """);
        var monitor = Monitor;

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        _asked.Should().BeEmpty();
        monitor.Current.Should().BeEmpty();
    }

    [Fact]
    public async Task should_check_again_at_once_when_the_address_changes()
    {
        var monitor = Monitor;
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        Given(Memory.Replace("memory.homelab.example", "memory2.homelab.example", StringComparison.Ordinal));
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        _asked.Should().Equal("https://memory.homelab.example/mcp", "https://memory2.homelab.example/mcp");
    }

    [Fact]
    public async Task should_forget_a_server_the_catalog_no_longer_declares()
    {
        var monitor = Monitor;
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        Given("apps:\n  - { id: blog, name: Blog }");
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        monitor.Current.Should().BeEmpty();
        await _store.Received(1).RetainAsync(Arg.Is<IReadOnlyCollection<string>>(ids => ids.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_start_from_what_the_database_remembers()
    {
        var remembered = new McpServerHealth("memory", McpHealthState.Down, Start.AddDays(-12), Start.AddDays(-12), Start.AddMinutes(-5), "timeout", null);
        _store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns([remembered]);
        var monitor = Monitor;

        await monitor.LoadAsync(TestContext.Current.CancellationToken);

        monitor.Current["memory"].Should().Be(remembered);

        // Still down at the next check: it has been down since the day it was first seen down.
        _status = HttpStatusCode.ServiceUnavailable;
        _body = "down";
        await monitor.RunDueAsync(TestContext.Current.CancellationToken);
        monitor.Current["memory"].Since.Should().Be(Start.AddDays(-12));
        monitor.Current["memory"].LastHealthyAt.Should().Be(Start.AddDays(-12));
    }

    [Fact]
    public async Task should_keep_the_health_in_memory_when_the_database_fails()
    {
        _store.SaveAsync(Arg.Any<McpServerHealth>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("disk full")));
        var monitor = Monitor;

        await monitor.RunDueAsync(TestContext.Current.CancellationToken);

        monitor.Current["memory"].State.Should().Be(McpHealthState.Healthy);
    }

    [Fact]
    public void should_list_the_servers_that_are_down_in_the_attention_list_with_the_time_since()
    {
        var health = Substitute.For<IMcpHealth>();
        health.Current.Returns(new Dictionary<string, McpServerHealth>
        {
            ["memory"] = new("memory", McpHealthState.Down, Start.AddDays(-12), Start.AddDays(-12), Start, "timeout", null),
            ["github"] = new("github", McpHealthState.Healthy, Start, Start, Start, null, 200),
        });
        _catalog.Current.Returns(State("""
            mcpServers:
              - { id: memory, name: Memory, transport: http, http: { url: "https://memory.homelab.example/mcp" } }
              - { id: github, transport: http, http: { url: "https://github.homelab.example/mcp" } }
            """));
        var inner = Substitute.For<Portarr.Core.Attention.IAttentionService>();
        inner.GetFindings().Returns([]);
        var subject = new McpAttentionService(inner, _catalog, health, _clock);

        var finding = subject.GetFindings().Should().ContainSingle().Subject;

        finding.Area.Should().Be("mcp");
        finding.Kind.Should().Be("mcp-server-down");
        finding.Subjects.Should().Equal("Memory (down for 12 d)");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, McpHealthState.Healthy)]
    [InlineData(HttpStatusCode.Unauthorized, McpHealthState.AuthenticationRequired)]
    [InlineData(HttpStatusCode.Forbidden, McpHealthState.AuthenticationRequired)]
    public void should_class_the_answer_of_a_server_that_is_up(HttpStatusCode status, McpHealthState expected)
    {
        var result = new CheckResult(Start, (int)status, TimeSpan.FromMilliseconds(30), null, null);

        McpHealthMonitor.Next("memory", null, result).State.Should().Be(expected);
    }

    [Fact]
    public void should_not_move_since_between_healthy_and_authentication_required()
    {
        var first = McpHealthMonitor.Next("memory", null, new CheckResult(Start, 200, null, null, null));

        var second = McpHealthMonitor.Next("memory", first, new CheckResult(Start.AddMinutes(5), 401, null, null, null));

        second.State.Should().Be(McpHealthState.AuthenticationRequired);
        second.Since.Should().Be(Start);
        second.LastHealthyAt.Should().Be(Start.AddMinutes(5));
    }

    [Fact]
    public void should_move_since_when_a_server_comes_back()
    {
        var down = McpHealthMonitor.Next("memory", null, new CheckResult(Start, null, null, "timeout", null));
        down.LastHealthyAt.Should().BeNull();

        var up = McpHealthMonitor.Next("memory", down, new CheckResult(Start.AddHours(2), 200, null, null, null));

        up.State.Should().Be(McpHealthState.Healthy);
        up.Since.Should().Be(Start.AddHours(2));
        up.LastHealthyAt.Should().Be(Start.AddHours(2));
    }

    [Theory]
    [InlineData(0, "1 min")]
    [InlineData(5, "5 min")]
    [InlineData(90, "1 h")]
    [InlineData(60 * 49, "2 d")]
    public void should_write_a_duration_as_short_as_the_list_wants(int minutes, string expected)
    {
        McpAttentionService.Duration(TimeSpan.FromMinutes(minutes)).Should().Be(expected);
    }

    private static CatalogState State(string yaml) => new(TestCatalog.Parse(yaml).Catalog, "catalog.yaml", true, [], null);

    private void Given(string yaml) => _catalog.Current.Returns(State(yaml));
}
