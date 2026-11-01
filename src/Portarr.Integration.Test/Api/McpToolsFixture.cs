using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Catalog;
using Portarr.Core.Workloads;

namespace Portarr.Integration.Test.Api;

/// <summary>The two MCP hosts of McpToolsFixture, built once for the class: one read-only, one with the write tools on.</summary>
public sealed class McpToolsHosts : IAsyncLifetime
{
    public const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-test";

    public const string Catalog = """
        # The catalog of the test fleet.
        environments:
          - { id: server-1, name: Server 1, docker: { url: "http://socket-proxy:2375" } }
        apps:
          - id: jellyfin   # media
            name: Jellyfin
            workloads: [{ runtime: docker@server-1, match: { name: jellyfin } }]
        """;

    private readonly TestDatabase _database;

    public McpToolsHosts(TestDatabase database)
    {
        _database = database;
    }

    public PortarrFactory Writable { get; private set; } = null!;

    public PortarrFactory ReadOnly { get; private set; } = null!;

    public string Folder { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        (Writable, Folder) = await CreateAsync(new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:ApiKey"] = Key, ["Mcp:WriteTools"] = "true", ["Catalog:Writable"] = "true" });
        (ReadOnly, _) = await CreateAsync(new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:ApiKey"] = Key });
    }

    public async ValueTask DisposeAsync()
    {
        await Writable.DisposeAsync();
        await ReadOnly.DisposeAsync();
    }

    /// <summary>Puts the writable host back as it started: the seeded catalog file, no audit trail, reloaded. Every test that writes starts here.</summary>
    public async Task ResetWritableAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(Folder, "catalog.yaml"), Catalog, TestContext.Current.CancellationToken);
        File.Delete(Path.Combine(Folder, "catalog.audit.jsonl"));
        File.Delete(Path.Combine(Folder, "catalog.audit.jsonl.1"));
        await Writable.Services.GetRequiredService<ICatalogService>().ReloadAsync(TestContext.Current.CancellationToken);
    }

    public async Task<(PortarrFactory Factory, string Folder)> CreateAsync(IReadOnlyDictionary<string, string?> settings)
    {
        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), Catalog);
        var factory = new PortarrFactory(options, settings);
        factory.Workloads.Workloads["server-1"] = [FakeWorkloadSource.Container("server-1", "jellyfin")];
        await factory.Services.GetRequiredService<IWorkloadInventory>().RefreshAsync(TestContext.Current.CancellationToken);
        return (factory, options.DataFolder);
    }
}

/// <summary>
/// V5 over Streamable HTTP, tool by tool: every read tool answers on a real catalog, and the write tools edit the
/// catalog file through the same editor as the API (versions, comments kept, audit), only when writes are on.
/// What each tool says is unit-tested (FleetDigestFixture, KnowledgeBaseFixture, CatalogEditorFixture). The hosts are
/// shared by the class; a test that writes resets the writable one first.
/// </summary>
[Trait("Category", "Integration")]
public sealed class McpToolsFixture : IClassFixture<McpToolsHosts>
{
    private const string Key = McpToolsHosts.Key;

    private readonly McpToolsHosts _hosts;

    public McpToolsFixture(McpToolsHosts hosts)
    {
        _hosts = hosts;
    }

    private PortarrFactory Writable => _hosts.Writable;

    private PortarrFactory ReadOnly => _hosts.ReadOnly;

    private string Folder => _hosts.Folder;

    [Fact]
    public async Task should_list_the_read_tools_and_add_the_write_tools_only_when_enabled()
    {
        var readOnly = await ToolNamesAsync(ReadOnly);
        var writable = await ToolNamesAsync(Writable);

        readOnly.Should().Contain(["get_overview", "list_apps", "get_app", "get_infrastructure", "find_workload", "get_data_coverage", "get_maintenance", "get_environment_knowledge"]);
        readOnly.Should().NotContain(["upsert_app", "remove_app", "set_portal", "set_notes"]);
        writable.Should().Contain(["get_catalog_item", "get_portal", "upsert_app", "remove_app", "upsert_stack", "set_portal", "set_notes"]);
    }

    // Each tool answers with a fact only it can say about the seeded fleet (one app, one unreachable Docker host).
    [Theory]
    [InlineData("get_overview", "{}", "\"apps\":{\"total\":1,\"healthy\":1")]
    [InlineData("list_apps", "{}", "\"id\":\"jellyfin\",\"name\":\"Jellyfin\",\"status\":\"Healthy\"")]
    [InlineData("get_app", """{"app":"jellyfin"}""", "\"image\":\"jellyfin:latest\"")]
    [InlineData("get_infrastructure", "{}", "\"error\":\"unreachable\"")]
    [InlineData("find_workload", """{"query":"jelly"}""", "\"apps\":[\"Jellyfin\"]")]
    [InlineData("get_data_coverage", "{}", "\"assets\":[]")]
    [InlineData("get_maintenance", "{}", "\"imagesUnknown\":1")]
    [InlineData("get_environment_knowledge", "{}", "\"apps\":[\"jellyfin\"]")]
    public async Task should_answer_each_read_tool_from_the_catalog(string tool, string arguments, string expected)
    {
        var text = await CallAsync(ReadOnly, tool, arguments);

        text.Should().Contain(expected, because: $"{tool} reads the fleet").And.NotContain(Key);
    }

    [Fact]
    public async Task should_refuse_an_unknown_app_with_the_known_ones()
    {
        var failure = await McpTestClient.CallRawAsync(ReadOnly.CreateClient(), "get_app", """{"app":"nope"}""", Key);

        McpTestClient.IsError(failure).Should().BeTrue();
        McpTestClient.TextOf(failure).Should().Contain("No app 'nope'");
    }

    [Fact]
    public async Task should_edit_the_catalog_file_with_versions_keeping_comments_and_audit_the_change()
    {
        await _hosts.ResetWritableAsync();
        var item = JsonDocument.Parse(await CallAsync(Writable, "get_catalog_item", """{"section":"apps"}""")).RootElement;
        var version = item.GetProperty("version").GetString()!;

        var written = JsonDocument.Parse(await CallAsync(Writable, "upsert_app", $$"""{"item":{"id":"radarr","name":"Radarr"},"version":"{{version}}"}""")).RootElement;
        var next = written.GetProperty("version").GetString()!;

        var file = await File.ReadAllTextAsync(Path.Combine(Folder, "catalog.yaml"), TestContext.Current.CancellationToken);
        file.Should().Contain("# The catalog of the test fleet.").And.Contain("# media").And.Contain("id: radarr");
        next.Should().NotBe(version);
        (await CallAsync(Writable, "list_apps", "{}")).Should().Contain("Radarr");

        var stale = await McpTestClient.CallRawAsync(Writable.CreateClient(), "upsert_app", $$"""{"item":{"id":"radarr","name":"Radarr 2"},"version":"{{version}}"}""", Key);
        McpTestClient.IsError(stale).Should().BeTrue("a stale version is refused");

        var removed = await McpTestClient.CallRawAsync(Writable.CreateClient(), "remove_app", $$"""{"id":"radarr","version":"{{next}}"}""", Key);
        McpTestClient.IsError(removed).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(Folder, "catalog.yaml"), TestContext.Current.CancellationToken)).Should().NotContain("radarr");

        var audit = (await File.ReadAllLinesAsync(Path.Combine(Folder, "catalog.audit.jsonl"), TestContext.Current.CancellationToken)).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        audit.Select(e => (e.GetProperty("channel").GetString(), e.GetProperty("operation").GetString(), e.GetProperty("id").GetString()))
            .Should().Equal([("mcp", "upsert", "radarr"), ("mcp", "remove", "radarr")]);
    }

    [Fact]
    public async Task should_set_the_name_of_the_portal_and_the_notes_of_an_app()
    {
        await _hosts.ResetWritableAsync();
        var version = JsonDocument.Parse(await CallAsync(Writable, "get_portal", "{}")).RootElement.GetProperty("version").GetString()!;

        var portal = JsonDocument.Parse(await CallAsync(Writable, "set_portal", $$"""{"version":"{{version}}","name":"My lab"}""")).RootElement;
        var notes = JsonDocument.Parse(await CallAsync(Writable, "set_notes", $$"""{"section":"apps","id":"jellyfin","notes":"Runs on the NAS.","version":"{{portal.GetProperty("version").GetString()}}"}""")).RootElement;

        notes.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        var file = await File.ReadAllTextAsync(Path.Combine(Folder, "catalog.yaml"), TestContext.Current.CancellationToken);
        file.Should().Contain("My lab").And.Contain("Runs on the NAS.");
    }

    [Fact]
    public async Task should_refuse_a_write_when_the_catalog_is_not_writable()
    {
        var (factory, _) = await _hosts.CreateAsync(new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:ApiKey"] = Key, ["Mcp:WriteTools"] = "true" });
        await using (factory)
        {
            var failure = await McpTestClient.CallRawAsync(factory.CreateClient(), "upsert_app", """{"item":{"id":"x","name":"X"},"version":"v"}""", Key);

            McpTestClient.IsError(failure).Should().BeTrue();
        }
    }

    private static Task<IReadOnlyList<string>> ToolNamesAsync(PortarrFactory factory) => McpTestClient.ToolNamesAsync(factory.CreateClient(), Key);

    private static Task<string> CallAsync(PortarrFactory factory, string tool, string arguments) => McpTestClient.CallAsync(factory.CreateClient(), tool, arguments, Key);
}
