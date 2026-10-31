using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Portarr.Api.V1.Catalog;

namespace Portarr.Integration.Test.Api;

/// <summary>
/// Catalog writes through the API and the MCP write tools (D-83 to D-86): off by default, keyed, versioned,
/// validated with field paths, audited, and the file's comments kept. The editor itself is unit-tested
/// (CatalogTextEditorFixture, CatalogEditorFixture).
/// </summary>
[Trait("Category", "Integration")]
public sealed class CatalogEditFixture : IAsyncLifetime
{
    private const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-test";

    private const string Catalog = """
        # Kept comment.
        environments:
          - { id: server-1, name: Server 1 }   # the box
        apps:
          - { id: jellyfin, name: Jellyfin }
        """;

    private readonly TestDatabase _database;
    private PortarrFactory _writable = null!;
    private PortarrFactory _readOnly = null!;
    private string _folder = null!;

    public CatalogEditFixture(TestDatabase database)
    {
        _database = database;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        (_writable, _folder) = await CreateAsync(new Dictionary<string, string?>
        {
            ["Catalog:Writable"] = "true",
            ["ApiKey"] = Key,
            ["Mcp:Enabled"] = "true",
            ["Mcp:ApiKey"] = Key,
            ["Mcp:WriteTools"] = "true",
        });
        (_readOnly, _) = await CreateAsync(new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:ApiKey"] = Key });
    }

    public async ValueTask DisposeAsync()
    {
        await _writable.DisposeAsync();
        await _readOnly.DisposeAsync();
    }

    [Fact]
    public async Task should_refuse_writes_when_off_and_hide_the_write_tools()
    {
        using var client = await _readOnly.CreateSignedInClientAsync();

        var response = await client.SendAsync(Notes("anything", "Nope.", Key), Token);
        var assistant = await client.GetStringAsync("/api/v1/assistant", Token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        assistant.Should().Contain("\"writeTools\":false").And.NotContain("upsert_app");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task should_refuse_a_missing_or_wrong_key(string? key)
    {
        using var admin = await _writable.CreateSignedInClientAsync();
        var version = await VersionAsync(admin);
        using var client = _writable.CreateClient();

        var response = await client.SendAsync(Notes(version, "Nope.", key), Token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle().Which.Scheme.Should().Be("Bearer");
    }

    [Fact]
    public async Task should_write_audit_and_refuse_stale_or_invalid_edits()
    {
        using var client = await _writable.CreateSignedInClientAsync();
        var version = await VersionAsync(client);

        var applied = await client.SendAsync(Notes(version, "The media server.", Key), Token);
        var stale = await client.SendAsync(Notes(version, "Again.", Key), Token);
        var invalid = await client.SendAsync(
            Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin", new { version = await VersionAsync(client), item = new { name = "Jellyfin", check = new { url = "ftp://nope" } } }, Key),
            Token);

        applied.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await applied.Content.ReadFromJsonAsync<CatalogEditResource>(Token))!;
        result.Should().Match<CatalogEditResource>(r => r.Status == "applied" && r.Version != version && r.Changes.Single() == "notes: added");
        var file = await File.ReadAllTextAsync(Path.Combine(_folder, "catalog.yaml"), Token);
        file.Should().StartWith("# Kept comment.").And.Contain("# the box").And.Contain("notes: The media server.");

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync(Token)).Should().Contain($"\"version\":\"{result.Version}\"");

        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync(Token)).Should().Contain("\"apps[jellyfin].check.url\"");

        var audit = await client.GetFromJsonAsync<List<CatalogAuditEntryResource>>("/api/v1/catalog/audit", Token);
        audit.Should().ContainSingle(e => e.Channel == "api").Which.Should().Match<CatalogAuditEntryResource>(e => e.Operation == "set-notes" && e.Id == "jellyfin" && e.VersionAfter == result.Version && e.User == "system");
    }

    [Fact]
    public async Task should_edit_through_the_mcp_write_tools_with_the_same_rules()
    {
        using var client = await _writable.CreateSignedInClientAsync();
        (await client.SendAsync(Rpc("initialize", Initialize), Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await CallAsync(client, "get_catalog_item", """{"section":"environments","id":"server-1"}""");
        var version = JsonDocument.Parse(read).RootElement.GetProperty("version").GetString();
        var added = await CallAsync(client, "add_link", $$"""{"item":{"id":"lan","from":"server-1","to":"server-1/nas","kind":"lan"},"version":"{{version}}"}""");
        var notes = await CallAsync(client, "set_notes", $$"""{"section":"environments","id":"server-1","machine":"nas","notes":"The NAS.","version":"{{version}}"}""");

        added.Should().Contain("is not a machine of an environment");
        notes.Should().Contain("\"status\":\"applied\"");
        var item = await client.GetFromJsonAsync<CatalogItemResource>("/api/v1/catalog/environments/server-1", Token);
        item!.Item["machines"]![0]!["notes"]!.ToString().Should().Be("The NAS.");
    }

    private const string Initialize = """{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}""";

    private static HttpRequestMessage Notes(string version, string notes, string? key) =>
        Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin/notes", new { version, notes }, key);

    private static HttpRequestMessage Request(HttpMethod method, string path, object body, string? key)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add("X-Api-Key", key);
        }

        return request;
    }

    private static async Task<string> VersionAsync(HttpClient client)
    {
        var status = await client.GetFromJsonAsync<CatalogStatusResource>("/api/v1/catalog/status", Token);
        return status!.Version!;
    }

    private static HttpRequestMessage Rpc(string method, string parameters)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent($$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{parameters}}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        request.Headers.Add("X-Api-Key", Key);
        return request;
    }

    /// <summary>The text of a tools/call answer (its result, or the error it reports).</summary>
    private static async Task<string> CallAsync(HttpClient client, string tool, string arguments)
    {
        var response = await client.SendAsync(Rpc("tools/call", $$"""{"name":"{{tool}}","arguments":{{arguments}}}"""), Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Token);
        var json = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("data: ", StringComparison.Ordinal))?["data: ".Length..] ?? body;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    private async Task<(PortarrFactory Factory, string Folder)> CreateAsync(IReadOnlyDictionary<string, string?> settings)
    {
        var options = await _database.CreateOptionsAsync();
        await File.WriteAllTextAsync(Path.Combine(options.DataFolder, "catalog.yaml"), Catalog);
        var factory = new PortarrFactory(options, settings);
        _ = factory.Services;
        return (factory, options.DataFolder);
    }
}
