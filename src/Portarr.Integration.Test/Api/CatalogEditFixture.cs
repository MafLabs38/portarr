using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portarr.Api.V1.Accounts;
using Portarr.Api.V1.Catalog;
using Portarr.Core.Catalog;

namespace Portarr.Integration.Test.Api;

/// <summary>The two hosts of CatalogEditFixture, built once for the class: catalog writes on, and off.</summary>
public sealed class CatalogEditHosts : IAsyncLifetime
{
    public const string Key = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-test";

    public const string Catalog = """
        # Kept comment.
        environments:
          - { id: server-1, name: Server 1 }   # the box
        apps:
          - { id: jellyfin, name: Jellyfin }
        """;

    private readonly TestDatabase _database;

    public CatalogEditHosts(TestDatabase database)
    {
        _database = database;
    }

    public PortarrFactory Writable { get; private set; } = null!;

    public PortarrFactory ReadOnly { get; private set; } = null!;

    public string Folder { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        (Writable, Folder) = await CreateAsync(new Dictionary<string, string?>
        {
            ["Catalog:Writable"] = "true",
            ["ApiKey"] = Key,
            ["Mcp:Enabled"] = "true",
            ["Mcp:ApiKey"] = Key,
            ["Mcp:WriteTools"] = "true",
        });
        (ReadOnly, _) = await CreateAsync(new Dictionary<string, string?> { ["Mcp:Enabled"] = "true", ["Mcp:ApiKey"] = Key });
    }

    public async ValueTask DisposeAsync()
    {
        await Writable.DisposeAsync();
        await ReadOnly.DisposeAsync();
    }

    /// <summary>Puts the writable host back as it started: the seeded catalog, no audit trail, reloaded. Every test that writes starts here.</summary>
    public async Task ResetWritableAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(Folder, "catalog.yaml"), Catalog, TestContext.Current.CancellationToken);
        File.Delete(Path.Combine(Folder, "catalog.audit.jsonl"));
        File.Delete(Path.Combine(Folder, "catalog.audit.jsonl.1"));
        await Writable.Services.GetRequiredService<ICatalogService>().ReloadAsync(TestContext.Current.CancellationToken);
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

/// <summary>
/// Catalog writes through the API and the MCP write tools (D-83 to D-86): off by default, keyed, versioned,
/// validated with field paths, audited, and the file's comments kept. The editor itself is unit-tested
/// (CatalogTextEditorFixture, CatalogEditorFixture). The hosts are shared by the class; a test that writes resets the
/// writable one first.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CatalogEditFixture : IClassFixture<CatalogEditHosts>
{
    private const string Key = CatalogEditHosts.Key;

    private readonly CatalogEditHosts _hosts;

    public CatalogEditFixture(CatalogEditHosts hosts)
    {
        _hosts = hosts;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private PortarrFactory _writable => _hosts.Writable;

    private PortarrFactory _readOnly => _hosts.ReadOnly;

    private string _folder => _hosts.Folder;

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
    public async Task should_let_the_role_decide_who_changes_how_a_host_is_reached_without_any_approval()
    {
        await _hosts.ResetWritableAsync();
        using var admin = await _writable.CreateSignedInClientAsync();
        var catalog = Path.Combine(_folder, "catalog.yaml");
        var adminKey = await PersonalKeyAsync(admin);
        object Item(string version) => new { version, item = new { name = "Jellyfin", check = new { url = "http://10.0.0.9:8096/health" } } };

        var refused = await admin.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin", Item(await VersionAsync(admin)), Key), Token);
        var unchanged = await File.ReadAllTextAsync(catalog, Token);
        var written = await admin.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin", Item(await VersionAsync(admin)), adminKey), Token);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a key whose role lacks settings.manage may not say how a host is reached");
        (await refused.Content.ReadAsStringAsync(Token)).Should().Contain("settings.manage");
        unchanged.Should().NotContain("10.0.0.9");
        written.StatusCode.Should().Be(HttpStatusCode.OK, "an administrator's key writes it at once: no approval step (D-134)");
        (await written.Content.ReadFromJsonAsync<CatalogEditResource>(Token))!.Status.Should().Be("applied");
        (await File.ReadAllTextAsync(catalog, Token)).Should().Contain("http://10.0.0.9:8096/health");
    }

    [Fact]
    public async Task should_keep_the_catalog_editor_from_an_operator_and_show_connections_only_to_whoever_may_change_them()
    {
        await _hosts.ResetWritableAsync();
        using var admin = await _writable.CreateSignedInClientAsync();
        using var operatorClient = await _writable.CreateSignedInClientAsync("Operator");
        await admin.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin", new { version = await VersionAsync(admin), item = new { name = "Jellyfin", check = new { url = "http://10.0.0.9:8096/health" } } }, null), Token);

        var asKey = await admin.SendAsync(Request(HttpMethod.Get, "/api/v1/catalog/apps", new { }, Key), Token);
        var asAdmin = await admin.GetFromJsonAsync<CatalogSectionResource>("/api/v1/catalog/apps", Token);

        (await asKey.Content.ReadFromJsonAsync<CatalogSectionResource>(Token))!.Items.Single(i => i["id"]!.ToString() == "jellyfin").ContainsKey("check").Should().BeFalse("a key that may not change how a host is reached does not read it");
        asAdmin!.Items.Single(i => i["id"]!.ToString() == "jellyfin")["check"]!["url"]!.ToString().Should().Be("http://10.0.0.9:8096/health");
        (await operatorClient.GetAsync("/api/v1/catalog/apps", Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "an operator reads the fleet, not the catalog editor");
        (await operatorClient.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin/notes", new { version = await VersionAsync(admin), notes = "x" }, null), Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "an operator changes nothing");
        (await operatorClient.GetAsync("/api/v1/apps", Token)).StatusCode.Should().Be(HttpStatusCode.OK, "an operator still reads the fleet");
        (await operatorClient.SendAsync(Request(HttpMethod.Get, "/api/v1/catalog/export", new { }, null), Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<string> PersonalKeyAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync("/api/v1/auth/keys", new { name = "assistant", scope = "full" }, Token);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<CreatedApiKeyResource>(Token))!.Value;
    }

    [Fact]
    public async Task should_write_how_a_host_is_reached_at_once_for_an_administrator_but_never_an_address_Portarr_must_not_call()
    {
        await _hosts.ResetWritableAsync();
        using var admin = await _writable.CreateSignedInClientAsync();
        var version = await VersionAsync(admin);

        var good = await admin.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin", new { version, item = new { name = "Jellyfin", check = new { url = "http://10.0.0.9:8096/health" } } }, null), Token);
        var metadata = await admin.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin", new { version = await VersionAsync(admin), item = new { name = "Jellyfin", check = new { url = "http://169.254.169.254/latest" } } }, null), Token);

        good.StatusCode.Should().Be(HttpStatusCode.OK);
        metadata.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await metadata.Content.ReadAsStringAsync(Token)).Should().Contain("169.254.169.254");
    }

    [Fact]
    public async Task should_remove_a_connection_setting_sent_as_null_and_keep_one_left_out()
    {
        await _hosts.ResetWritableAsync();
        using var admin = await _writable.CreateSignedInClientAsync();
        var catalog = Path.Combine(_folder, "catalog.yaml");
        await admin.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin", new { version = await VersionAsync(admin), item = new { name = "Jellyfin", check = new { url = "http://10.0.0.9:8096/health" } } }, null), Token);

        var leftOut = await admin.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin", new { version = await VersionAsync(admin), item = new { name = "Jellyfin 2" } }, Key), Token);
        var kept = await File.ReadAllTextAsync(catalog, Token);
        var removed = await admin.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin", new { version = await VersionAsync(admin), item = new { name = "Jellyfin", check = (object?)null } }, null), Token);

        leftOut.StatusCode.Should().Be(HttpStatusCode.OK);
        kept.Should().Contain("10.0.0.9", "a key left out is kept");
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await File.ReadAllTextAsync(catalog, Token)).Should().NotContain("10.0.0.9");
    }

    [Fact]
    public async Task should_check_an_item_without_writing_it()
    {
        await _hosts.ResetWritableAsync();
        using var operatorClient = await _writable.CreateSignedInClientAsync();
        var catalog = Path.Combine(_folder, "catalog.yaml");
        var before = await File.ReadAllTextAsync(catalog, Token);
        var version = await VersionAsync(operatorClient);

        var valid = await operatorClient.SendAsync(Request(HttpMethod.Post, "/api/v1/catalog/apps/jellyfin/validate", new { version, item = new { name = "Jellyfin 3" } }, null), Token);
        var withCheck = await operatorClient.SendAsync(Request(HttpMethod.Post, "/api/v1/catalog/apps/jellyfin/validate", new { version, item = new { name = "Jellyfin", check = new { url = "http://10.0.0.9:8096/health" } } }, null), Token);
        var invalid = await operatorClient.SendAsync(Request(HttpMethod.Post, "/api/v1/catalog/apps/jellyfin/validate", new { version, item = new { name = "Jellyfin", stack = "nowhere" } }, null), Token);

        valid.StatusCode.Should().Be(HttpStatusCode.OK);
        (await valid.Content.ReadAsStringAsync(Token)).Should().Contain("\"valid\"");
        withCheck.StatusCode.Should().Be(HttpStatusCode.OK);
        (await withCheck.Content.ReadAsStringAsync(Token)).Should().Contain("\"valid\"", "a dry run that sets a connection writes nothing either");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await File.ReadAllTextAsync(catalog, Token)).Should().Be(before);
    }

    [Fact]
    public async Task should_answer_each_kind_of_refusal_with_its_own_status_and_title()
    {
        await _hosts.ResetWritableAsync();
        using var client = await _writable.CreateSignedInClientAsync();
        var version = await VersionAsync(client);

        var missing = await client.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/nope/notes", new { version, notes = "x" }, Key), Token);
        var exists = await client.SendAsync(Request(HttpMethod.Post, "/api/v1/catalog/apps", new { version, item = new { id = "jellyfin", name = "Again" } }, Key), Token);
        var stale = await client.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin/notes", new { version = "0000000000000000", notes = "x" }, Key), Token);
        var readOnly = await _readOnly.CreateSignedInClientAsync();
        var refused = await readOnly.SendAsync(Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin/notes", new { version, notes = "x" }, Key), Token);
        readOnly.Dispose();

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exists.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await exists.Content.ReadAsStringAsync(Token)).Should().Contain("Already exists");
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync(Token)).Should().Contain("Version mismatch");
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task should_write_audit_and_refuse_stale_or_invalid_edits()
    {
        await _hosts.ResetWritableAsync();
        using var client = await _writable.CreateSignedInClientAsync();
        var version = await VersionAsync(client);

        var applied = await client.SendAsync(Notes(version, "The media server.", Key), Token);
        var stale = await client.SendAsync(Notes(version, "Again.", Key), Token);
        var invalid = await client.SendAsync(
            Request(HttpMethod.Put, "/api/v1/catalog/apps/jellyfin", new { version = await VersionAsync(client), item = new { name = "Jellyfin", urls = new[] { new { href = "ftp://nope" } } } }, Key),
            Token);

        applied.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await applied.Content.ReadFromJsonAsync<CatalogEditResource>(Token))!;
        result.Should().Match<CatalogEditResource>(r => r.Status == "applied" && r.Version != version && r.Changes.Single() == "notes: added");
        var file = await File.ReadAllTextAsync(Path.Combine(_folder, "catalog.yaml"), Token);
        file.Should().StartWith("# Kept comment.").And.Contain("# the box").And.Contain("notes: The media server.");

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync(Token)).Should().Contain($"\"version\":\"{result.Version}\"");

        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync(Token)).Should().Contain("apps[jellyfin]").And.Contain("ftp");

        var audit = await client.GetFromJsonAsync<List<CatalogAuditEntryResource>>("/api/v1/catalog/audit", Token);
        audit.Should().ContainSingle(e => e.Channel == "api").Which.Should().Match<CatalogAuditEntryResource>(e => e.Operation == "set-notes" && e.Id == "jellyfin" && e.VersionAfter == result.Version && e.User == "system");
    }

    [Fact]
    public async Task should_write_an_mcp_server_and_a_workstation_and_check_without_writing()
    {
        await _hosts.ResetWritableAsync();
        using var client = await _writable.CreateSignedInClientAsync();
        var file = Path.Combine(_folder, "catalog.yaml");
        string Version(string text) => JsonDocument.Parse(text).RootElement.GetProperty("version").GetString()!;
        var adminKey = await PersonalKeyAsync(client);
        var version = Version(await McpTestClient.CallAsync(client, "get_catalog_item", """{"section":"apps"}""", adminKey));
        var refused = await McpTestClient.CallRawAsync(client, "upsert_mcp_server", $$$"""{"item":{"id":"docs","name":"Docs","transport":"http","http":{"url":"https://10.0.0.20:8080/mcp"}},"version":"{{{version}}}"}""", Key);

        var checkedOnly = JsonDocument.Parse(await McpTestClient.CallAsync(client, "get_catalog_item_check", $$$"""{"section":"mcpServers","item":{"id":"docs","name":"Docs","transport":"http","http":{"url":"https://10.0.0.20:8080/mcp"}},"version":"{{{version}}}"}""", adminKey)).RootElement;
        var checkedText = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        var server = JsonDocument.Parse(await McpTestClient.CallAsync(client, "upsert_mcp_server", $$$"""{"item":{"id":"docs","name":"Docs","transport":"http","http":{"url":"https://10.0.0.20:8080/mcp"}},"version":"{{{version}}}"}""", adminKey)).RootElement;
        var written = JsonDocument.Parse(await McpTestClient.CallAsync(client, "upsert_workstation", $$"""{"item":{"id":"desk","name":"Desk","os":"linux","clients":["claude-code"]},"version":"{{Version(server.GetRawText())}}"}""", adminKey)).RootElement;

        McpTestClient.IsError(refused).Should().BeTrue("a key whose role lacks settings.manage may not say how a server is reached");
        McpTestClient.TextOf(refused).Should().Contain("settings.manage");
        checkedOnly.GetProperty("status").GetString().Should().Be("valid", "the check says what a write would do");
        checkedText.Should().NotContain("10.0.0.20", "a check writes nothing");
        server.GetProperty("status").GetString().Should().Be("applied", "no approval step (D-134)");
        written.GetProperty("status").GetString().Should().Be("applied");
        var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        text.Should().Contain("id: desk").And.Contain("https://10.0.0.20:8080/mcp");
    }

    [Fact]
    public async Task should_edit_through_the_mcp_write_tools_with_the_same_rules()
    {
        await _hosts.ResetWritableAsync();
        using var client = await _writable.CreateSignedInClientAsync();
        var read = await McpTestClient.CallAsync(client, "get_catalog_item", """{"section":"environments","id":"server-1"}""", Key);
        var version = JsonDocument.Parse(read).RootElement.GetProperty("version").GetString();
        var added = await McpTestClient.CallRawAsync(client, "add_link", $$"""{"item":{"id":"lan","from":"server-1","to":"server-1/nas","kind":"lan"},"version":"{{version}}"}""", Key);
        var notes = await McpTestClient.CallAsync(client, "set_notes", $$"""{"section":"environments","id":"server-1","machine":"nas","notes":"The NAS.","version":"{{version}}"}""", Key);

        McpTestClient.IsError(added).Should().BeTrue("the link names a machine the catalog does not know yet");
        McpTestClient.TextOf(added).Should().Contain("is not a machine of an environment");
        notes.Should().Contain("\"status\":\"applied\"");
        var item = await client.GetFromJsonAsync<CatalogItemResource>("/api/v1/catalog/environments/server-1", Token);
        item!.Item["machines"]![0]!["notes"]!.ToString().Should().Be("The NAS.");
    }

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
}
