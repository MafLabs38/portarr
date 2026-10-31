using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Core.Workloads;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// V5 over Streamable HTTP, tool by tool: every read tool answers on a real catalog, and the write tools edit the
/// catalog file through the same editor as the API (versions, comments kept, audit), only when writes are on.
/// What each tool says is unit-tested (FleetDigestFixture, KnowledgeBaseFixture, CatalogEditorFixture).
/// </summary>
[Trait("Category", "Integration")]
public sealed class McpToolsFixture : IAsyncLifetime
{
    private const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-test";

    private const string Catalog = """
        # The catalog of the test fleet.
        environments:
          - { id: server-1, name: Server 1, docker: { url: "http://socket-proxy:2375" } }
        apps:
          - id: jellyfin   # media
            name: Jellyfin
            workloads: [{ runtime: docker@server-1, match: { name: jellyfin } }]
        """;

    private readonly TestDatabase _database;
    private PortarrFactory _writable = null!;
    private PortarrFactory _readOnly = null!;
    private string _folder = null!;

    public McpToolsFixture(TestDatabase database)
    {
        _database = database;
    }

    public async ValueTask InitializeAsync()
    {
        _writable = await CreateAsync(new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:ApiKey"] = Key, ["Mcp:WriteTools"] = "true", ["Catalog:Writable"] = "true" }, folder => _folder = folder);
        _readOnly = await CreateAsync(new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:ApiKey"] = Key }, _ => { });
    }

    public async ValueTask DisposeAsync()
    {
        await _writable.DisposeAsync();
        await _readOnly.DisposeAsync();
    }

    [Fact]
    public async Task should_list_the_read_tools_and_add_the_write_tools_only_when_enabled()
    {
        var readOnly = await ToolNamesAsync(_readOnly);
        var writable = await ToolNamesAsync(_writable);

        readOnly.Should().Contain(["get_overview", "list_apps", "get_app", "get_infrastructure", "find_workload", "get_data_coverage", "get_maintenance", "get_environment_knowledge"]);
        readOnly.Should().NotContain(["upsert_app", "remove_app", "set_portal", "set_notes"]);
        writable.Should().Contain(["get_catalog_item", "get_portal", "upsert_app", "remove_app", "upsert_stack", "set_portal", "set_notes"]);
    }

    [Theory]
    [InlineData("get_overview", "{}", "needsAttention")]
    [InlineData("list_apps", "{}", "Jellyfin")]
    [InlineData("get_app", """{"app":"jellyfin"}""", "Jellyfin")]
    [InlineData("get_infrastructure", "{}", "server-1")]
    [InlineData("find_workload", """{"query":"jelly"}""", "jellyfin")]
    [InlineData("get_data_coverage", "{}", "followed")]
    [InlineData("get_maintenance", "{}", "repositories")]
    [InlineData("get_environment_knowledge", "{}", "Server 1")]
    public async Task should_answer_each_read_tool_from_the_catalog(string tool, string arguments, string expected)
    {
        var text = await CallAsync(_readOnly, tool, arguments);

        text.Should().Contain(expected, because: $"{tool} reads the fleet").And.NotContain(Key);
    }

    [Fact]
    public async Task should_refuse_an_unknown_app_with_the_known_ones()
    {
        var failure = await CallRawAsync(_readOnly, "get_app", """{"app":"nope"}""");

        IsError(failure).Should().BeTrue();
        failure.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString().Should().Contain("No app 'nope'");
    }

    [Fact]
    public async Task should_edit_the_catalog_file_with_versions_keeping_comments_and_audit_the_change()
    {
        var item = JsonDocument.Parse(await CallAsync(_writable, "get_catalog_item", """{"section":"apps"}""")).RootElement;
        var version = item.GetProperty("version").GetString()!;

        var written = JsonDocument.Parse(await CallAsync(_writable, "upsert_app", $$"""{"item":{"id":"radarr","name":"Radarr"},"version":"{{version}}"}""")).RootElement;
        var next = written.GetProperty("version").GetString()!;

        var file = await File.ReadAllTextAsync(Path.Combine(_folder, "catalog.yaml"), TestContext.Current.CancellationToken);
        file.Should().Contain("# The catalog of the test fleet.").And.Contain("# media").And.Contain("id: radarr");
        next.Should().NotBe(version);
        (await CallAsync(_writable, "list_apps", "{}")).Should().Contain("Radarr");

        var stale = await CallRawAsync(_writable, "upsert_app", $$"""{"item":{"id":"radarr","name":"Radarr 2"},"version":"{{version}}"}""");
        IsError(stale).Should().BeTrue("a stale version is refused");

        var removed = await CallRawAsync(_writable, "remove_app", $$"""{"id":"radarr","version":"{{next}}"}""");
        IsError(removed).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(_folder, "catalog.yaml"), TestContext.Current.CancellationToken)).Should().NotContain("radarr");

        var audit = await File.ReadAllTextAsync(Path.Combine(_folder, "catalog.audit.jsonl"), TestContext.Current.CancellationToken);
        audit.Should().Contain("\"channel\":\"mcp\"").And.Contain("radarr");
    }

    [Fact]
    public async Task should_set_the_notes_of_an_app_and_the_name_of_the_portal()
    {
        var version = JsonDocument.Parse(await CallAsync(_writable, "get_portal", "{}")).RootElement.GetProperty("version").GetString()!;

        var portal = JsonDocument.Parse(await CallAsync(_writable, "set_portal", $$"""{"version":"{{version}}","name":"My lab"}""")).RootElement;
        var notes = JsonDocument.Parse(await CallAsync(_writable, "set_notes", $$"""{"section":"apps","id":"jellyfin","notes":"Runs on the NAS.","version":"{{portal.GetProperty("version").GetString()}}"}""")).RootElement;

        notes.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        var file = await File.ReadAllTextAsync(Path.Combine(_folder, "catalog.yaml"), TestContext.Current.CancellationToken);
        file.Should().Contain("My lab").And.Contain("Runs on the NAS.");
    }

    [Fact]
    public async Task should_refuse_a_write_when_the_catalog_is_not_writable()
    {
        var factory = await CreateAsync(new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:ApiKey"] = Key, ["Mcp:WriteTools"] = "true" }, _ => { });
        await using (factory)
        {
            var failure = await CallRawAsync(factory, "upsert_app", """{"item":{"id":"x","name":"X"},"version":"v"}""");

            IsError(failure).Should().BeTrue();
        }
    }

    private static async Task<IReadOnlyList<string>> ToolNamesAsync(PortarrFactory factory)
    {
        var response = await SendAsync(factory, "tools/list", "{}");
        return response.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();
    }

    private static async Task<string> CallAsync(PortarrFactory factory, string tool, string arguments)
    {
        var response = await CallRawAsync(factory, tool, arguments);
        IsError(response).Should().BeFalse($"{tool}: {response.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()}");
        return response.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    private static bool IsError(JsonElement response) => response.GetProperty("result").TryGetProperty("isError", out var isError) && isError.GetBoolean();

    private static Task<JsonElement> CallRawAsync(PortarrFactory factory, string tool, string arguments) =>
        SendAsync(factory, "tools/call", $$"""{"name":"{{tool}}","arguments":{{arguments}}}""");

    private static async Task<JsonElement> SendAsync(PortarrFactory factory, string method, string parameters)
    {
        using var client = factory.CreateClient();
        var initialize = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = Json("initialize", """{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}""") };
        Headers(initialize);
        (await client.SendAsync(initialize, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = Json(method, parameters) };
        Headers(request);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal))?["data: ".Length..] ?? body;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static StringContent Json(string method, string parameters) =>
        new($$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{parameters}}}""", Encoding.UTF8, "application/json");

    private static void Headers(HttpRequestMessage request)
    {
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        request.Headers.Add("X-Api-Key", Key);
    }

    private async Task<PortarrFactory> CreateAsync(IReadOnlyDictionary<string, string?> settings, Action<string> folder)
    {
        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), Catalog);
        folder(options.DataFolder);
        var factory = new PortarrFactory(options, settings);
        factory.Workloads.Workloads["server-1"] = [FakeWorkloadSource.Container("server-1", "jellyfin")];
        await factory.Services.GetRequiredService<IWorkloadInventory>().RefreshAsync(TestContext.Current.CancellationToken);
        return factory;
    }
}
