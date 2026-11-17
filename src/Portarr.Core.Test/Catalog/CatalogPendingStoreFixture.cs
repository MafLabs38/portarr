using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Portarr.Core.Catalog.Editing;
using Portarr.Core.Configuration;

namespace Portarr.Core.Test.Catalog;

/// <summary>The waiting changes are kept in a file next to the catalog: capped overall and per author, and never silently lost.</summary>
public sealed class CatalogPendingStoreFixture : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("portarr-pending-store-").FullName;
    private readonly CatalogPendingStore _subject;

    public CatalogPendingStoreFixture()
    {
        _subject = new CatalogPendingStore(Options.Create(new PortarrOptions { DataFolder = _folder }));
    }

    private CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static PendingChange Change(string id, string item, string? user = "alice", string channel = "mcp")
    {
        return new PendingChange(id, DateTimeOffset.UnixEpoch, channel, user, null, "apps", item, "upsert", null, new JsonObject(), ["added"]);
    }

    [Fact]
    public async Task should_keep_the_waiting_changes_across_a_restart()
    {
        await _subject.AddAsync(Change("a1", "sonarr"), Token);

        var afterRestart = new CatalogPendingStore(Options.Create(new PortarrOptions { DataFolder = _folder }));

        (await afterRestart.ListAsync(Token)).Select(p => p.Id).Should().Equal("a1");
    }

    [Fact]
    public async Task should_not_remove_what_does_not_wait()
    {
        await _subject.AddAsync(Change("a1", "sonarr"), Token);

        (await _subject.RemoveAsync("unknown", Token)).Should().BeFalse();
        (await _subject.RemoveAsync("a1", Token)).Should().BeTrue();
    }

    [Fact]
    public async Task should_treat_a_caller_with_no_user_as_one_author_per_channel()
    {
        await _subject.AddAsync(Change("a1", "sonarr", user: null), Token);
        await _subject.AddAsync(Change("a2", "sonarr", user: null), Token);
        await _subject.AddAsync(Change("a3", "sonarr", user: null, channel: "api"), Token);

        (await _subject.ListAsync(Token)).Select(p => p.Id).Should().Equal("a2", "a3");
    }

    [Fact]
    public async Task should_keep_one_change_per_item_and_author_the_newer_replacing_the_older()
    {
        await _subject.AddAsync(Change("a1", "sonarr"), Token);
        await _subject.AddAsync(Change("a2", "sonarr"), Token);
        await _subject.AddAsync(Change("b1", "sonarr", user: "bob"), Token);

        (await _subject.ListAsync(Token)).Select(p => p.Id).Should().Equal("a2", "b1");
    }

    [Fact]
    public async Task should_refuse_an_author_past_their_share_without_blocking_the_others()
    {
        for (var i = 0; i < CatalogPendingStore.CapacityPerAuthor; i++)
        {
            (await _subject.AddAsync(Change($"a{i}", $"item-{i}"), Token)).Should().BeTrue();
        }

        (await _subject.AddAsync(Change("late", "one-more"), Token)).Should().BeFalse();
        (await _subject.AddAsync(Change("other", "one-more", user: "bob"), Token)).Should().BeTrue("another author still has room");
    }

    [Fact]
    public async Task should_refuse_a_change_when_the_whole_queue_is_full()
    {
        for (var i = 0; i < CatalogPendingStore.Capacity; i++)
        {
            (await _subject.AddAsync(Change($"c{i}", $"item-{i}", user: $"user-{i}"), Token)).Should().BeTrue();
        }

        (await _subject.AddAsync(Change("late", "one-more", user: "someone"), Token)).Should().BeFalse();
    }

    [Fact]
    public async Task should_move_an_unreadable_file_aside_instead_of_overwriting_it_silently()
    {
        var file = Path.Combine(_folder, "catalog.pending.json");
        await File.WriteAllTextAsync(file, "{ not json", Token);

        (await _subject.ListAsync(Token)).Should().BeEmpty();

        Directory.GetFiles(_folder, "catalog.pending.json.corrupt-*").Should().ContainSingle();
        File.Exists(file).Should().BeFalse();
    }
}
