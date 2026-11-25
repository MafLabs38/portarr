using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portarr.Core.Datastore;
using Portarr.Core.Datastore.Migration;
using Portarr.Core.Notifications;

namespace Portarr.Integration.Test.Datastore;

/// <summary>What the notifications remember so that a restart sends nothing twice and the caps hold (ADR 0022): the real SQL and the migration, on SQLite and on PostgreSQL.</summary>
[Trait("Category", "Integration")]
public sealed class NotificationStoreFixture : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 9, 30, 15, TimeSpan.Zero);

    private readonly TestDatabase _testDatabase;
    private NotificationStore _store = null!;

    public NotificationStoreFixture(TestDatabase testDatabase)
    {
        _testDatabase = testDatabase;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var database = new Database(Options.Create(await _testDatabase.CreateOptionsAsync()));
        new MigrationController(database, NullLogger<MigrationController>.Instance).Migrate();
        _store = new NotificationStore(database);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task should_read_back_an_episode_with_its_times_and_its_empty_columns()
    {
        var mailed = new NotificationEpisode("apps", Now.AddDays(-2), Now, 2, Now.AddHours(-24), Now.AddHours(-24));
        var waiting = new NotificationEpisode("data", Now, Now, 0, null, null);

        await _store.SaveEpisodeAsync(mailed, Token);
        await _store.SaveEpisodeAsync(waiting, Token);

        var all = await _store.ReadEpisodesAsync(Token);
        all.Single(e => e.Key == "apps").Should().BeEquivalentTo(mailed);
        all.Single(e => e.Key == "data").Should().BeEquivalentTo(waiting);
    }

    [Fact]
    public async Task should_replace_the_episode_of_a_category_when_it_is_saved_again_and_forget_it_when_it_ends()
    {
        await _store.SaveEpisodeAsync(new NotificationEpisode("replace", Now, Now, 0, null, null), Token);
        var later = new NotificationEpisode("replace", Now, Now.AddMinutes(5), 1, Now.AddMinutes(5), Now.AddMinutes(5));

        await _store.SaveEpisodeAsync(later, Token);

        (await _store.ReadEpisodesAsync(Token)).Where(e => e.Key == "replace").Should().ContainSingle().Which.Should().BeEquivalentTo(later);

        await _store.DeleteEpisodeAsync("replace", Token);
        (await _store.ReadEpisodesAsync(Token)).Should().NotContain(e => e.Key == "replace");
    }

    [Fact]
    public async Task should_not_read_an_episode_that_says_it_was_mailed_without_saying_when()
    {
        await _store.SaveEpisodeAsync(new NotificationEpisode("inconsistent", Now, Now, 2, null, null), Token);

        (await _store.ReadEpisodesAsync(Token)).Should().NotContain(e => e.Key == "inconsistent");
    }

    [Fact]
    public async Task should_count_the_attempts_since_a_moment_for_one_category_or_for_all_of_them()
    {
        await _store.RecordAsync(Entry("apps", Now.AddHours(-30), true), Token);
        await _store.RecordAsync(Entry("apps", Now.AddHours(-2), false), Token);
        await _store.RecordAsync(Entry("infrastructure", Now.AddHours(-1), true), Token);
        await _store.RecordAsync(Entry(null, Now.AddMinutes(-5), true, "test"), Token);

        (await _store.CountSinceAsync(Now.AddDays(-1), null, Token)).Should().Be(3, "a failed attempt and the test mail count too");
        (await _store.CountSinceAsync(Now.AddDays(-1), "apps", Token)).Should().Be(1);
        (await _store.CountSinceAsync(Now.AddDays(-2), "apps", Token)).Should().Be(2);
    }

    [Fact]
    public async Task should_return_the_latest_attempts_newest_first_with_the_reason_of_a_failure()
    {
        await _store.RecordAsync(Entry("data", Now.AddHours(-3), false, "category", "The mail server could not be reached."), Token);
        await _store.RecordAsync(Entry("data", Now.AddHours(-2), true, "category"), Token);

        var latest = await _store.LatestAsync(2, Token);

        latest.Select(e => e.Delivered).Should().Equal(true, false);
        latest[0].Id.Should().BeGreaterThan(latest[1].Id);
        latest[1].Should().BeEquivalentTo(new NotificationLogEntry(latest[1].Id, "category", "data", "data@20261124T0900Z", "danger", 0, Now.AddHours(-3), false, "The mail server could not be reached."));
        (await _store.LatestAsync(1, Token)).Should().HaveCount(1);
    }

    [Fact]
    public async Task should_forget_the_attempts_older_than_a_moment_and_keep_the_others()
    {
        await _store.RecordAsync(Entry(null, Now.AddDays(-40), true, "test"), Token);
        await _store.RecordAsync(Entry(null, Now.AddDays(-1), true, "test"), Token);

        await _store.PruneAsync(Now.AddDays(-30), Token);

        (await _store.LatestAsync(10, Token)).Should().ContainSingle().Which.SentAt.Should().Be(Now.AddDays(-1));
    }

    private static NotificationLogEntry Entry(string? category, DateTimeOffset at, bool delivered, string kind = "category", string? error = null) =>
        new(0, kind, category, category is null ? null : $"{category}@20261124T0900Z", category is null ? "info" : "danger", 0, at, delivered, error);
}
