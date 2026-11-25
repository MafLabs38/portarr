using Dapper;
using MafLabs.Notifications;
using Portarr.Core.Accounts;
using Portarr.Core.Datastore;

namespace Portarr.Core.Notifications;

/// <summary>The two sorts of mail in the log.</summary>
public static class NotificationLogKinds
{
    /// <summary>A mail about a category of findings.</summary>
    public const string Category = "category";

    /// <summary>The test mail of the settings page.</summary>
    public const string Test = "test";
}

/// <summary>
/// A mail that was tried, delivered or not. <c>Kind</c> is <c>category</c> or <c>test</c>; <c>Category</c> the id of the category (null for a test);
/// <c>Level</c> the level of the mail (<c>info</c>, <c>warning</c>, <c>danger</c>). <c>Error</c> is a sentence of Portarr or of the shared package,
/// never a server's text and never a secret.
/// </summary>
public sealed record NotificationLogEntry(
    long Id,
    string Kind,
    string? Category,
    string? EpisodeKey,
    string? Level,
    int Reminder,
    DateTimeOffset SentAt,
    bool Delivered,
    string? Error);

/// <summary>
/// A category that has had findings above its minimum level and still has: when it began, when it was last seen, and what it got. The key is the id of the
/// category (<c>data</c>): one episode, one mail, whatever the number of findings in it. <c>Mails</c> counts the mails that were delivered,
/// <c>LastAttemptAt</c> the last try, delivered or not.
/// </summary>
public sealed record NotificationEpisode(
    string Key,
    DateTimeOffset StartedAt,
    DateTimeOffset LastSeenAt,
    int Mails,
    DateTimeOffset? LastMailedAt,
    DateTimeOffset? LastAttemptAt)
{
    /// <summary>What names this episode in the log: the category and the moment it began, so that two episodes of the same category stay apart.</summary>
    public string Name => $"{Key}@{StartedAt.UtcDateTime:yyyyMMddTHHmmZ}";

    /// <summary>The last try did not leave: it is later than the last mail that did.</summary>
    public bool LastAttemptFailed => LastAttemptAt is { } attempt && (LastMailedAt is null || attempt > LastMailedAt);

    /// <summary>The episode as the package's decider reads it.</summary>
    public EpisodeState ToState() => new(StartedAt, LastSeenAt, Mails, LastMailedAt, LastAttemptAt);
}

/// <summary>What was tried and what is going on, kept so that the caps and the reminders hold across a restart (ADR 0022).</summary>
public interface INotificationStore
{
    Task<IReadOnlyList<NotificationEpisode>> ReadEpisodesAsync(CancellationToken cancellationToken);

    /// <summary>Stores the episode, replacing the one of the same key.</summary>
    Task SaveEpisodeAsync(NotificationEpisode episode, CancellationToken cancellationToken);

    Task DeleteEpisodeAsync(string key, CancellationToken cancellationToken);

    Task RecordAsync(NotificationLogEntry entry, CancellationToken cancellationToken);

    /// <summary>How many mails were tried since a moment, for one category or, when none is named, all of them (the test mail included).</summary>
    Task<int> CountSinceAsync(DateTimeOffset since, string? category, CancellationToken cancellationToken);

    /// <summary>The latest attempts, newest first.</summary>
    Task<IReadOnlyList<NotificationLogEntry>> LatestAsync(int count, CancellationToken cancellationToken);

    /// <summary>Forgets the attempts older than a moment.</summary>
    Task PruneAsync(DateTimeOffset before, CancellationToken cancellationToken);
}

public sealed class NotificationStore : INotificationStore
{
    private readonly IDatabase _database;

    public NotificationStore(IDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<NotificationEpisode>> ReadEpisodesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<EpisodeRow>(new CommandDefinition(
            """SELECT "EpisodeKey", "StartedAt", "LastSeenAt", "Mails", "LastMailedAt", "LastAttemptAt" FROM "NotificationEpisode" """,
            cancellationToken: cancellationToken));
        return rows.Select(ReadEpisode).OfType<NotificationEpisode>().ToList();
    }

    public async Task SaveEpisodeAsync(NotificationEpisode episode, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var parameters = new
        {
            key = episode.Key,
            started = Iso.Format(episode.StartedAt),
            seen = Iso.Format(episode.LastSeenAt),
            mails = episode.Mails,
            mailed = Iso.Format(episode.LastMailedAt),
            attempt = Iso.Format(episode.LastAttemptAt),
        };

        // Replace the row: UPDATE first, INSERT when nothing was there (the same SQL on SQLite and PostgreSQL).
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """UPDATE "NotificationEpisode" SET "StartedAt" = @started, "LastSeenAt" = @seen, "Mails" = @mails, "LastMailedAt" = @mailed, "LastAttemptAt" = @attempt WHERE "EpisodeKey" = @key""",
            parameters,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """INSERT INTO "NotificationEpisode" ("EpisodeKey", "StartedAt", "LastSeenAt", "Mails", "LastMailedAt", "LastAttemptAt") VALUES (@key, @started, @seen, @mails, @mailed, @attempt)""",
                parameters,
                cancellationToken: cancellationToken));
        }
    }

    public async Task DeleteEpisodeAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """DELETE FROM "NotificationEpisode" WHERE "EpisodeKey" = @key""",
            new { key },
            cancellationToken: cancellationToken));
    }

    public async Task RecordAsync(NotificationLogEntry entry, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """INSERT INTO "NotificationLog" ("Kind", "Category", "EpisodeKey", "Level", "Reminder", "SentAt", "Delivered", "Error") VALUES (@kind, @category, @episode, @level, @reminder, @at, @delivered, @error)""",
            new
            {
                kind = entry.Kind,
                category = entry.Category,
                episode = entry.EpisodeKey,
                level = entry.Level,
                reminder = entry.Reminder,
                at = Iso.Format(entry.SentAt),
                delivered = entry.Delivered,
                error = entry.Error,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CountSinceAsync(DateTimeOffset since, string? category, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        // The text of a UTC instant in ISO 8601 has a fixed width, so that comparing the texts compares the moments.
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            category is null
                ? """SELECT COUNT(*) FROM "NotificationLog" WHERE "SentAt" >= @since"""
                : """SELECT COUNT(*) FROM "NotificationLog" WHERE "SentAt" >= @since AND "Category" = @category""",
            new { since = Iso.Format(since), category },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<NotificationLogEntry>> LatestAsync(int count, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<LogRow>(new CommandDefinition(
            """SELECT "Id", "Kind", "Category", "EpisodeKey", "Level", "Reminder", "SentAt", "Delivered", "Error" FROM "NotificationLog" ORDER BY "Id" DESC LIMIT @count""",
            new { count },
            cancellationToken: cancellationToken));
        return rows.Select(row => new NotificationLogEntry(row.Id, row.Kind, row.Category, row.EpisodeKey, row.Level, row.Reminder, Iso.Parse(row.SentAt), row.Delivered, row.Error)).ToList();
    }

    public async Task PruneAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """DELETE FROM "NotificationLog" WHERE "SentAt" < @before""",
            new { before = Iso.Format(before) },
            cancellationToken: cancellationToken));
    }

    /// <summary>A row as an episode, or null when it cannot be read: one bad row costs that category its memory, not every category.</summary>
    private static NotificationEpisode? ReadEpisode(EpisodeRow row)
    {
        try
        {
            var mailed = Iso.ParseOrNull(row.LastMailedAt);
            return row.Mails > 0 && mailed is null
                ? null
                : new NotificationEpisode(row.EpisodeKey, Iso.Parse(row.StartedAt), Iso.Parse(row.LastSeenAt), row.Mails, mailed, Iso.ParseOrNull(row.LastAttemptAt));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal sealed class EpisodeRow
    {
        public string EpisodeKey { get; set; } = string.Empty;

        public string StartedAt { get; set; } = string.Empty;

        public string LastSeenAt { get; set; } = string.Empty;

        public int Mails { get; set; }

        public string? LastMailedAt { get; set; }

        public string? LastAttemptAt { get; set; }
    }

    internal sealed class LogRow
    {
        public long Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string? Category { get; set; }

        public string? EpisodeKey { get; set; }

        public string? Level { get; set; }

        public int Reminder { get; set; }

        public string SentAt { get; set; } = string.Empty;

        public bool Delivered { get; set; }

        public string? Error { get; set; }
    }
}
