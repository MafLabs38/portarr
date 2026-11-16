using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;
using Portarr.Core.Configuration;

namespace Portarr.Core.Test.Catalog;

/// <summary>
/// How a host is reached can be written, but not by anyone alone: a person who may reconfigure writes it, anyone else (an
/// assistant) leaves a change that an administrator approves. Every address in it passes the guardrails first. The app's
/// <c>check</c> stands for the protected settings here (the connector sections need a connector registered).
/// </summary>
public sealed class CatalogPendingFixture : IDisposable
{
    private static readonly CatalogActor Assistant = new("mcp", "10.0.0.5", "chat:abc");
    private static readonly CatalogActor Person = new("api", "10.0.0.6", null, "alice", Reconfigure: true);

    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-pending-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 31, 10, 0, 0, TimeSpan.Zero));
    private readonly PortarrOptions _options;
    private readonly CatalogService _catalogService;
    private readonly CatalogEditor _subject;

    public CatalogPendingFixture()
    {
        _options = new PortarrOptions { DataFolder = _folder, Catalog = { Writable = true, Backups = 2 } };
        var options = Options.Create(_options);
        _catalogService = new CatalogService(options, new CatalogParserService([]), _clock, NullLogger<CatalogService>.Instance);
        _subject = new CatalogEditor(options, _catalogService, new CatalogParserService([]), new CatalogAuditLog(options), new CatalogPendingStore(options), _clock, NullLogger<CatalogEditor>.Instance);
        File.WriteAllText(CatalogFile, "apps:\n  - { id: sonarr, name: Sonarr }\n");
    }

    private string CatalogFile => Path.Combine(_folder, "catalog.yaml");

    private CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _subject.Dispose();
        _catalogService.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private async Task<CatalogEditResult> UpsertAsync(CatalogActor actor, string checkUrl, bool dryRun = false)
    {
        var item = new JsonObject { ["name"] = "Sonarr", ["check"] = new JsonObject { ["url"] = checkUrl } };
        return await _subject.EditAsync(new CatalogEdit("apps", "sonarr", "upsert", await _subject.GetVersionAsync(Token), actor, CatalogEdits.Upsert("sonarr", item), dryRun), Token);
    }

    [Fact]
    public async Task should_keep_a_change_of_how_a_host_is_reached_for_approval_when_an_assistant_asks()
    {
        var before = await File.ReadAllTextAsync(CatalogFile, Token);

        var result = await UpsertAsync(Assistant, "http://10.0.0.7:8989/ping");

        result.Status.Should().Be(CatalogEditStatus.Pending);
        result.PendingId.Should().NotBeNullOrEmpty();
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().Be(before, "nothing is written until an administrator approves");
        var waiting = (await _subject.ListPendingAsync(true, Token)).Should().ContainSingle().Subject;
        waiting.Channel.Should().Be("mcp");
        waiting.Via.Should().Be("chat:abc");
        waiting.After!["check"]!["url"]!.ToString().Should().Be("http://10.0.0.7:8989/ping");
    }

    private async Task<CatalogEditResult> UpsertMcpServerAsync(CatalogActor actor, JsonObject server)
    {
        return await _subject.EditAsync(new CatalogEdit("mcpServers", "docs", "upsert", await _subject.GetVersionAsync(Token), actor, CatalogEdits.Upsert("docs", server)), Token);
    }

    [Fact]
    public async Task should_keep_an_mcp_server_for_approval_and_create_its_section_when_an_assistant_adds_it()
    {
        var server = new JsonObject
        {
            ["name"] = "docs",
            ["transport"] = "http",
            ["http"] = new JsonObject { ["url"] = "https://docs.homelab.example/mcp" },
        };

        var waiting = await UpsertMcpServerAsync(Assistant, server);
        var approved = await _subject.ApproveAsync(waiting.PendingId!, Person, Token);

        waiting.Status.Should().Be(CatalogEditStatus.Pending);
        approved.Status.Should().Be(CatalogEditStatus.Applied);
        var text = await File.ReadAllTextAsync(CatalogFile, Token);
        text.Should().Contain("mcpServers:").And.Contain("https://docs.homelab.example/mcp");
        text.Should().Contain("id: sonarr", "the apps stay as they were");
    }

    [Fact]
    public async Task should_refuse_a_loopback_address_in_an_mcp_server_url_but_accept_one_in_the_arguments_of_a_local_program()
    {
        var remote = new JsonObject { ["name"] = "docs", ["transport"] = "http", ["http"] = new JsonObject { ["url"] = "http://127.0.0.1:9/mcp" } };
        var local = new JsonObject
        {
            ["name"] = "docs",
            ["transport"] = "stdio",
            ["stdio"] = new JsonObject { ["command"] = "npx", ["args"] = new JsonArray("-y", "@playwright/mcp", "--cdp-endpoint", "http://127.0.0.1:9222") },
        };

        var refused = await UpsertMcpServerAsync(Person, remote);
        var accepted = await UpsertMcpServerAsync(Assistant, local);

        refused.Status.Should().Be(CatalogEditStatus.Invalid);
        refused.Errors.Should().ContainSingle(e => e.Contains("127.0.0.1"));
        accepted.Status.Should().Be(CatalogEditStatus.Pending, "a command that runs on the operator's machines waits for approval");
    }

    [Fact]
    public async Task should_write_the_change_at_once_when_the_author_may_reconfigure()
    {
        var result = await UpsertAsync(Person, "http://10.0.0.7:8989/ping");

        result.Status.Should().Be(CatalogEditStatus.Applied);
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().Contain("http://10.0.0.7:8989/ping");
        (await _subject.ListPendingAsync(true, Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_apply_a_waiting_change_when_an_administrator_approves_it_and_clear_it()
    {
        var pending = await UpsertAsync(Assistant, "http://10.0.0.7:8989/ping");

        var result = await _subject.ApproveAsync(pending.PendingId!, Person with { Reconfigure = false }, Token);

        result.Status.Should().Be(CatalogEditStatus.Applied, "approving is the reconfiguring: the approver's own rights do not matter once the endpoint has checked them");
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().Contain("http://10.0.0.7:8989/ping");
        (await _subject.ListPendingAsync(true, Token)).Should().BeEmpty();
        var audit = await new CatalogAuditLog(Options.Create(_options)).ReadAsync(10, Token);
        audit.Select(a => a.Operation).Should().Contain(["propose:upsert", "approve:upsert"]);
    }

    [Fact]
    public async Task should_approve_a_change_over_an_item_whose_other_keys_were_edited_since()
    {
        var pending = await UpsertAsync(Assistant, "http://10.0.0.7:8989/ping");
        await _subject.EditAsync(new CatalogEdit("apps", "sonarr", "set-notes", await _subject.GetVersionAsync(Token), Person, CatalogEdits.SetNotes("apps", "sonarr", "Edited meanwhile.")), Token);

        var result = await _subject.ApproveAsync(pending.PendingId!, Person, Token);

        result.Status.Should().Be(CatalogEditStatus.Applied);
        var file = await File.ReadAllTextAsync(CatalogFile, Token);
        file.Should().Contain("http://10.0.0.7:8989/ping").And.Contain("Edited meanwhile.", "the note written since is kept");
    }

    [Fact]
    public async Task should_refuse_to_approve_a_change_when_the_same_key_changed_since()
    {
        var pending = await UpsertAsync(Assistant, "http://10.0.0.7:8989/ping");
        await UpsertAsync(Person, "http://10.0.0.8:8989/ping");

        var result = await _subject.ApproveAsync(pending.PendingId!, Person, Token);

        result.Status.Should().Be(CatalogEditStatus.Conflict);
        result.Errors.Single().Should().Contain("check");
        (await _subject.ListPendingAsync(true, Token)).Should().ContainSingle("the person decides: reject it and ask again");
    }

    [Fact]
    public async Task should_drop_a_rejected_change_without_writing_it()
    {
        var pending = await UpsertAsync(Assistant, "http://10.0.0.7:8989/ping");

        (await _subject.RejectAsync(pending.PendingId!, Person, Token)).Should().BeTrue();
        (await _subject.RejectAsync(pending.PendingId!, Person, Token)).Should().BeFalse("it is gone");

        (await _subject.ListPendingAsync(true, Token)).Should().BeEmpty();
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().NotContain("check");
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://127.0.0.1:8484/api")]
    [InlineData("http://user:secret@10.0.0.7/ping")]
    [InlineData("ftp://10.0.0.7/ping")]
    [InlineData("http://localhost:8484/")]
    [InlineData("http://metadata.google.internal/")]
    [InlineData("http://[::1]:9/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[::ffff:169.254.169.254]/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://localhost./")]
    [InlineData("http://app.localhost/")]
    [InlineData("http:\\\\169.254.169.254:2375")]
    public async Task should_refuse_an_address_Portarr_must_not_call_whoever_asks(string url)
    {
        (await UpsertAsync(Person, url)).Status.Should().Be(CatalogEditStatus.Invalid);
        (await UpsertAsync(Assistant, url)).Status.Should().Be(CatalogEditStatus.Invalid, "a change that cannot be approved is not queued");

        (await _subject.ListPendingAsync(true, Token)).Should().BeEmpty();
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().NotContain("check");
    }

    [Fact]
    public async Task should_leave_out_how_a_host_is_reached_from_what_a_waiting_change_shows_to_an_author_who_may_not_reconfigure()
    {
        await UpsertAsync(Person, "http://10.0.0.7:8989/ping");
        await UpsertAsync(Assistant, "http://10.0.0.8:8989/ping");

        var shown = (await _subject.ListPendingAsync(false, Token)).Should().ContainSingle().Subject;

        shown.Before!.ContainsKey("check").Should().BeFalse();
        shown.After!.ContainsKey("check").Should().BeFalse();
        shown.After["name"]!.ToString().Should().Be("Sonarr", "the rest of the item stays readable");
        shown.Changes.Should().NotContain(line => line.StartsWith("check", StringComparison.Ordinal)).And.Contain(line => line.Contains("connection settings", StringComparison.Ordinal));
        (await _subject.ListPendingAsync(true, Token)).Single().After!["check"]!["url"]!.ToString().Should().Be("http://10.0.0.8:8989/ping", "an administrator sees everything");
    }

    [Fact]
    public async Task should_not_judge_again_an_address_the_item_already_held_when_another_setting_changes()
    {
        // A loopback address written by hand (the file is the operator's): editing the name must not be blocked by it.
        await File.WriteAllTextAsync(CatalogFile, "apps:\n  - { id: sonarr, name: Sonarr, check: { url: 'http://127.0.0.1:8989/ping' } }\n", Token);
        var item = new JsonObject { ["name"] = "Sonarr TV", ["check"] = new JsonObject { ["url"] = "http://127.0.0.1:8989/ping" } };

        var result = await _subject.EditAsync(new CatalogEdit("apps", "sonarr", "upsert", await _subject.GetVersionAsync(Token), Assistant, CatalogEdits.Upsert("sonarr", item)), Token);

        result.Status.Should().Be(CatalogEditStatus.Applied, "no protected setting changed");
    }

    [Fact]
    public async Task should_keep_who_asked_and_through_what_in_the_audit_line_of_an_approval()
    {
        var pending = await UpsertAsync(Assistant, "http://10.0.0.7:8989/ping");

        await _subject.ApproveAsync(pending.PendingId!, Person, Token);

        var audit = await File.ReadAllTextAsync(Path.Combine(_folder, "catalog.audit.jsonl"), Token);
        audit.Should().Contain($"approved:{pending.PendingId} asked by mcp via chat:abc");
    }

    [Fact]
    public async Task should_audit_the_change_a_newer_one_replaces()
    {
        var first = await UpsertAsync(Assistant, "http://10.0.0.7:8989/ping");
        var second = await UpsertAsync(Assistant, "http://10.0.0.8:8989/ping");

        (await _subject.ListPendingAsync(true, Token)).Should().ContainSingle().Which.Id.Should().Be(second.PendingId);
        (await File.ReadAllTextAsync(Path.Combine(_folder, "catalog.audit.jsonl"), Token)).Should().Contain($"superseded:{first.PendingId} by {second.PendingId}");
    }

    [Fact]
    public async Task should_refuse_a_change_whose_address_is_not_acceptable_when_it_is_approved()
    {
        var pending = await UpsertAsync(Assistant, "http://10.0.0.7:8989/ping");
        var file = Path.Combine(_folder, "catalog.pending.json");
        await File.WriteAllTextAsync(file, (await File.ReadAllTextAsync(file, Token)).Replace("http://10.0.0.7:8989/ping", "http://169.254.169.254/", StringComparison.Ordinal), Token);

        var result = await _subject.ApproveAsync(pending.PendingId!, Person, Token);

        result.Status.Should().Be(CatalogEditStatus.Invalid);
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().NotContain("169.254");
    }

    [Fact]
    public async Task should_refuse_to_approve_while_the_catalog_is_read_only()
    {
        var pending = await UpsertAsync(Assistant, "http://10.0.0.7:8989/ping");
        _options.Catalog.Writable = false;

        (await _subject.ApproveAsync(pending.PendingId!, Person, Token)).Status.Should().Be(CatalogEditStatus.ReadOnly);
    }

    [Fact]
    public async Task should_accept_a_docker_proxy_over_tcp_from_an_administrator()
    {
        await File.WriteAllTextAsync(CatalogFile, "environments:\n  - { id: host-1, name: Host 1, docker: { url: 'tcp://10.0.0.10:2375' } }\n", Token);
        var item = new JsonObject { ["name"] = "Host 1", ["docker"] = new JsonObject { ["url"] = "tcp://10.0.0.11:2375" } };

        var result = await _subject.EditAsync(new CatalogEdit("environments", "host-1", "upsert", await _subject.GetVersionAsync(Token), Person, CatalogEdits.Upsert("host-1", item), DryRun: true), Token);

        result.Errors.Should().NotContain(error => error.Contains("scheme", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8484/")]
    [InlineData("http://169.254.169.254/latest")]
    [InlineData("http://localhost:3000/")]
    public async Task should_refuse_a_link_an_address_check_would_fetch_whoever_writes_it(string href)
    {
        var item = new JsonObject { ["name"] = "Sonarr", ["urls"] = new JsonArray(new JsonObject { ["label"] = "Public", ["href"] = href }) };

        foreach (var actor in new[] { Person, Assistant })
        {
            var result = await _subject.EditAsync(new CatalogEdit("apps", "sonarr", "upsert", await _subject.GetVersionAsync(Token), actor, CatalogEdits.Upsert("sonarr", item)), Token);

            result.Status.Should().Be(CatalogEditStatus.Invalid);
            result.Errors.Should().ContainSingle().Which.Should().StartWith("urls[0].href");
        }
    }

    [Fact]
    public async Task should_write_a_link_to_a_public_or_lan_address_at_once_for_anyone()
    {
        var item = new JsonObject { ["name"] = "Sonarr", ["urls"] = new JsonArray(new JsonObject { ["label"] = "Public", ["href"] = "https://sonarr.homelab.example" }, new JsonObject { ["label"] = "LAN", ["href"] = "http://10.0.0.7:8989" }) };

        var result = await _subject.EditAsync(new CatalogEdit("apps", "sonarr", "upsert", await _subject.GetVersionAsync(Token), Assistant, CatalogEdits.Upsert("sonarr", item)), Token);

        result.Status.Should().Be(CatalogEditStatus.Applied, "a link is content, not a connection setting");
    }

    [Fact]
    public async Task should_check_an_edit_without_writing_it()
    {
        var before = await File.ReadAllTextAsync(CatalogFile, Token);

        var valid = await UpsertAsync(Person, "http://10.0.0.7:8989/ping", dryRun: true);
        var pending = await UpsertAsync(Assistant, "http://10.0.0.7:8989/ping", dryRun: true);
        var invalid = await UpsertAsync(Person, "http://169.254.169.254/", dryRun: true);

        valid.Status.Should().Be(CatalogEditStatus.Valid);
        pending.Status.Should().Be(CatalogEditStatus.Pending);
        pending.PendingId.Should().BeNull("a dry run queues nothing");
        invalid.Status.Should().Be(CatalogEditStatus.Invalid);
        (await File.ReadAllTextAsync(CatalogFile, Token)).Should().Be(before);
        (await _subject.ListPendingAsync(true, Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_check_an_edit_even_when_the_catalog_is_read_only()
    {
        _options.Catalog.Writable = false;

        (await UpsertAsync(Person, "http://10.0.0.7:8989/ping", dryRun: true)).Status.Should().Be(CatalogEditStatus.Valid);
        (await UpsertAsync(Person, "http://10.0.0.7:8989/ping")).Status.Should().Be(CatalogEditStatus.ReadOnly);
    }

    [Fact]
    public async Task should_answer_the_catalog_as_it_is_for_a_download()
    {
        var (version, text) = await _subject.ExportAsync(Token);

        text.Should().Be(await File.ReadAllTextAsync(CatalogFile, Token));
        version.Should().Be(await _subject.GetVersionAsync(Token));
    }

    [Fact]
    public async Task should_list_the_items_with_how_hosts_are_reached_only_for_who_may_reconfigure()
    {
        await UpsertAsync(Person, "http://10.0.0.7:8989/ping");

        var (_, withConnections) = await _subject.GetItemsAsync("apps", withConnections: true, Token);
        var (_, without) = await _subject.GetItemsAsync("apps", withConnections: false, Token);

        withConnections.Single()["check"].Should().NotBeNull();
        without.Single()["check"].Should().BeNull();
    }
}
