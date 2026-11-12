using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;
using Portarr.Core.Mcp.Capture;
using Portarr.Test.Common;

namespace Portarr.Core.Test.Mcp.Capture;

/// <summary>What accepting does to the workstations when the catalog keeps changing, and what it refuses (D-112, D-116).</summary>
public sealed class McpCaptureAcceptFixture
{
    private const string Catalog = """
        mcpServers:
          - id: memory
            transport: http
            http: { url: "https://memory.homelab.example/mcp" }
        workstations:
          - id: dev-laptop
            os: linux
            clients: [claude-code]
            mcp: [memory]
        """;

    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly ICatalogEditor _editor = Substitute.For<ICatalogEditor>();
    private readonly IMcpCaptureStore _store = Substitute.For<IMcpCaptureStore>();
    private readonly McpCaptureService _service;
    private readonly McpCaptureRow _row;

    public McpCaptureAcceptFixture()
    {
        var catalogService = Substitute.For<ICatalogService>();
        catalogService.Current.Returns(new CatalogState(TestCatalog.Parse(Catalog).Catalog, "/config/catalog.yaml", true, [], Now));
        _editor.GetVersionAsync(Arg.Any<CancellationToken>()).Returns("v1");
        var server = new CapturedServer("memory", "http", "https://memory.homelab.example/mcp", null, [], [], []);
        _row = new McpCaptureRow(1, 1, "dev-laptop", "claude-code", "user", Now, "assistant", "memory", McpIdentity.Of(server), server);
        _store.PendingAsync(Arg.Any<CancellationToken>()).Returns([_row]);
        _service = new McpCaptureService(catalogService, _editor, _store, new FakeTimeProvider(Now));
    }

    private string Key => McpIdentity.KeyOf(_row.Identity);

    private string Version => McpCaptureService.VersionOf([_row]);

    [Fact]
    public async Task should_report_a_workstation_whose_edit_kept_conflicting_instead_of_dropping_it()
    {
        _editor.EditAsPersonAsync(Arg.Any<CatalogEdit>(), Arg.Any<CancellationToken>()).Returns(new CatalogEditResult(CatalogEditStatus.Conflict, "v1", [], []));

        var result = await _service.AcceptAsync(Key, new AcceptRequest(McpAcceptModes.Existing, null, null, null, true, Version), "person", null, TestContext.Current.CancellationToken);

        result.WorkstationsUpdated.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().StartWith("dev-laptop:").And.Contain("kept changing");
    }

    [Fact]
    public async Task should_refuse_a_decision_taken_on_a_version_that_is_no_longer_the_current_one()
    {
        var act = () => _service.AcceptAsync(Key, new AcceptRequest(McpAcceptModes.Existing, null, null, null, false, "0000000000000000"), "person", null, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<McpCaptureException>()).Which.Message.Should().Contain("handed over again");
        await _editor.DidNotReceive().EditAsPersonAsync(Arg.Any<CatalogEdit>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_refuse_a_decision_that_carries_no_version()
    {
        var act = () => _service.AcceptAsync(Key, new AcceptRequest(McpAcceptModes.Existing, null, null, null, false), "person", null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<McpCaptureException>();
    }

    [Fact]
    public async Task should_refuse_a_hand_over_when_the_inbox_is_full()
    {
        var waiting = Enumerable.Range(0, McpCaptureService.MaxWaiting).Select(i => _row with { RowId = 100 + i, Workstation = $"machine-{i}" }).ToList();
        _store.PendingAsync(Arg.Any<CancellationToken>()).Returns(waiting);
        var server = new CapturedServer("another", "http", "https://another.homelab.example/mcp", null, [], [], []);

        var act = () => _service.SubmitAsync(new CaptureRequest("dev-laptop", "claude-code", "user", [server]), "assistant", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<McpCaptureException>()).Which.Message.Should().Contain("accept or dismiss");
        await _store.DidNotReceive().SubmitAsync(Arg.Any<CaptureRequest>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
