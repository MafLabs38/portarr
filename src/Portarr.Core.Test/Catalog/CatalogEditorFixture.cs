using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;
using Portarr.Core.Configuration;

namespace Portarr.Core.Test.Catalog;

public sealed class CatalogEditorFixture : IDisposable
{
    private const string Catalog = """
        # Comment kept.
        environments:
          - { id: server-1, name: Server 1 }   # the box
        apps:
          - { id: sonarr, name: Sonarr }
        """;

    private static readonly CatalogActor Actor = new("api", "10.0.0.5");

    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-editor-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 31, 10, 0, 0, TimeSpan.Zero));
    private readonly PortarrOptions _options;
    private readonly CatalogService _catalogService;
    private readonly CatalogEditor _subject;

    public CatalogEditorFixture()
    {
        _options = new PortarrOptions { DataFolder = _folder, Catalog = { Writable = true, Backups = 2 } };
        var options = Options.Create(_options);
        _catalogService = new CatalogService(options, new CatalogParserService([]), _clock, NullLogger<CatalogService>.Instance);
        _subject = new CatalogEditor(options, _catalogService, new CatalogParserService([]), new CatalogAuditLog(options), _clock, NullLogger<CatalogEditor>.Instance);
        File.WriteAllText(CatalogFile, Catalog);
    }

    private string CatalogFile => Path.Combine(_folder, "catalog.yaml");

    private CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _subject.Dispose();
        _catalogService.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    private async Task<CatalogEditResult> SetNotesAsync(string notes, string? version = null)
    {
        version ??= await _subject.GetVersionAsync(Token);
        return await _subject.EditAsync(new CatalogEdit("apps", "sonarr", "set-notes", version, Actor, CatalogEdits.SetNotes("apps", "sonarr", notes)), Token);
    }

    [Fact]
    public async Task should_write_the_edit_keep_the_previous_version_audit_it_and_reload_the_catalog()
    {
        var before = await _subject.GetVersionAsync(Token);

        var result = await SetNotesAsync("Downloads TV shows.");

        result.Status.Should().Be(CatalogEditStatus.Applied);
        result.Changes.Should().Equal("notes: added");
        result.Version.Should().NotBe(before).And.Be(await _subject.GetVersionAsync(Token));
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().StartWith("# Comment kept.\n").And.Contain("# the box");
        File.ReadAllText(Directory.GetFiles(_folder, "catalog.yaml.bak-*").Single()).Should().Be(Catalog);
        _catalogService.Current.Catalog.Apps.Single().Notes.Should().Be("Downloads TV shows.");
        _catalogService.Current.Version.Should().Be(result.Version);

        var audit = await new CatalogAuditLog(Options.Create(_options)).ReadAsync(10, Token);
        audit.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CatalogAuditEntry(_clock.GetUtcNow(), "api", "10.0.0.5", "set-notes", "apps", "sonarr", before, result.Version, ["notes: added"]));
    }

    [Fact]
    public async Task should_refuse_a_stale_version_and_leave_the_file_untouched()
    {
        var stale = await _subject.GetVersionAsync(Token);
        await SetNotesAsync("First.");
        var written = await File.ReadAllTextAsync(CatalogFile, Token);

        var result = await SetNotesAsync("Second.", stale);

        result.Status.Should().Be(CatalogEditStatus.Conflict);
        result.Errors.Single().Should().Contain("changed since version");
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().Be(written);
    }

    [Fact]
    public async Task should_refuse_an_edit_the_parser_rejects_with_its_errors()
    {
        var version = await _subject.GetVersionAsync(Token);

        var result = await _subject.EditAsync(
            new CatalogEdit("apps", "radarr", "upsert", version, Actor, CatalogEdits.Upsert("radarr", Json("""{ "name": "Radarr", "stack": "nope" }"""))),
            Token);

        result.Status.Should().Be(CatalogEditStatus.Invalid);
        result.Errors.Should().ContainSingle().Which.Should().Contain("stack 'nope' is not declared");
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().Be(Catalog);
        Directory.GetFiles(_folder, "catalog.yaml.bak-*").Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_notes_that_hold_a_secret()
    {
        var result = await SetNotesAsync("Login: admin / password=hunter2");

        result.Status.Should().Be(CatalogEditStatus.Invalid);
        result.Errors.Single().Should().Contain("looks like it contains a secret").And.NotContain("hunter2");
    }

    [Fact]
    public async Task should_refuse_everything_when_the_catalog_is_not_writable()
    {
        _options.Catalog.Writable = false;

        var result = await SetNotesAsync("Nope.");

        result.Status.Should().Be(CatalogEditStatus.ReadOnly);
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().Be(Catalog);
    }

    [Fact]
    public async Task should_require_the_version()
    {
        var result = await _subject.EditAsync(new CatalogEdit("apps", "sonarr", "remove", null, Actor, CatalogEdits.Remove("apps", "sonarr")), Token);

        result.Status.Should().Be(CatalogEditStatus.Invalid);
        result.Errors.Single().Should().Contain("version is required");
    }

    [Fact]
    public async Task should_not_write_an_edit_that_changes_nothing()
    {
        var version = await _subject.GetVersionAsync(Token);

        var result = await _subject.EditAsync(new CatalogEdit("apps", "sonarr", "upsert", version, Actor, CatalogEdits.Upsert("sonarr", Json("""{ "name": "Sonarr" }"""))), Token);

        result.Status.Should().Be(CatalogEditStatus.Unchanged);
        Directory.GetFiles(_folder, "catalog.yaml.bak-*").Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_only_the_configured_number_of_previous_versions()
    {
        foreach (var note in new[] { "One.", "Two.", "Three." })
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            (await SetNotesAsync(note)).Status.Should().Be(CatalogEditStatus.Applied);
        }

        var backups = Directory.GetFiles(_folder, "catalog.yaml.bak-*").Order(StringComparer.Ordinal).ToList();
        backups.Should().HaveCount(2);
        File.ReadAllText(backups[^1]).Should().Contain("notes: Two.");
    }

    [Fact]
    public async Task should_serialize_concurrent_edits_so_only_one_wins_a_version()
    {
        var version = await _subject.GetVersionAsync(Token);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => SetNotesAsync($"Writer {i}.", version)));

        results.Count(r => r.Status == CatalogEditStatus.Applied).Should().Be(1);
        results.Count(r => r.Status == CatalogEditStatus.Conflict).Should().Be(4);
    }

    [Fact]
    public async Task should_add_and_remove_a_machine_of_an_environment()
    {
        var version = await _subject.GetVersionAsync(Token);
        var machine = Json("""{ "notes": "The VPS.", "access": { "ssh": { "alias": "vps", "user": "ubuntu", "key": "~/.ssh/vps" } } }""");

        var added = await _subject.EditAsync(new CatalogEdit("environments", "server-1", "upsert-machine", version, Actor, CatalogEdits.UpsertMachine("server-1", "vps", machine)), Token);

        added.Status.Should().Be(CatalogEditStatus.Applied, string.Join("; ", added.Errors));
        var environment = _catalogService.Current.Catalog.Environments.Single();
        environment.Machines.Should().ContainSingle().Which.Access!.Ssh!.Key.Should().Be("~/.ssh/vps");

        var removed = await _subject.EditAsync(new CatalogEdit("environments", "server-1", "remove-machine", added.Version, Actor, CatalogEdits.RemoveMachine("server-1", "vps")), Token);

        removed.Status.Should().Be(CatalogEditStatus.Applied);
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().Be(Catalog);
    }

    [Fact]
    public async Task should_say_when_the_item_exists_or_is_missing()
    {
        var version = await _subject.GetVersionAsync(Token);

        var exists = await _subject.EditAsync(new CatalogEdit("apps", "sonarr", "add", version, Actor, CatalogEdits.Add("apps", "sonarr", Json("""{ "name": "Sonarr" }"""))), Token);
        var missing = await _subject.EditAsync(new CatalogEdit("apps", "nope", "remove", version, Actor, CatalogEdits.Remove("apps", "nope")), Token);
        var otherId = await _subject.EditAsync(new CatalogEdit("apps", "sonarr", "upsert", version, Actor, CatalogEdits.Upsert("sonarr", Json("""{ "id": "radarr" }"""))), Token);

        exists.Status.Should().Be(CatalogEditStatus.AlreadyExists);
        missing.Status.Should().Be(CatalogEditStatus.NotFound);
        otherId.Status.Should().Be(CatalogEditStatus.Invalid);
    }
}
