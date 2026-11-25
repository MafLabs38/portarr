using MafLabs.Notifications;
using Portarr.Core.Attention;
using Portarr.Core.Maintenance;
using Portarr.Core.Notifications;

namespace Portarr.Core.Test.Notifications;

/// <summary>The store in memory: the same behaviour as the database one (episodes by key, a log, counts since a moment), for the notifier tests.</summary>
internal sealed class FakeNotificationStore : INotificationStore
{
    public Dictionary<string, NotificationEpisode> Episodes { get; } = new(StringComparer.Ordinal);

    public List<NotificationLogEntry> Log { get; } = [];

    public Task<IReadOnlyList<NotificationEpisode>> ReadEpisodesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationEpisode>>([.. Episodes.Values]);

    public Task SaveEpisodeAsync(NotificationEpisode episode, CancellationToken cancellationToken)
    {
        Episodes[episode.Key] = episode;
        return Task.CompletedTask;
    }

    public Task DeleteEpisodeAsync(string key, CancellationToken cancellationToken)
    {
        _ = Episodes.Remove(key);
        return Task.CompletedTask;
    }

    public Task RecordAsync(NotificationLogEntry entry, CancellationToken cancellationToken)
    {
        Log.Add(entry with { Id = Log.Count + 1 });
        return Task.CompletedTask;
    }

    public Task<int> CountSinceAsync(DateTimeOffset since, string? category, CancellationToken cancellationToken) =>
        Task.FromResult(Log.Count(e => e.SentAt >= since && (category is null || e.Category == category)));

    public Task<IReadOnlyList<NotificationLogEntry>> LatestAsync(int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationLogEntry>>([.. Enumerable.Reverse(Log).Take(count)]);

    public Task PruneAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        _ = Log.RemoveAll(e => e.SentAt < before);
        return Task.CompletedTask;
    }
}

/// <summary>Settings that are what the test says: no database, no configuration. The categories start at the defaults of Portarr (maintenance off).</summary>
internal sealed class FakeNotificationSettings : INotificationSettings
{
    public FakeNotificationSettings(PortarrNotificationOptions options)
    {
        Options = options;
        foreach (var category in TestCategories.Declared().All)
        {
            Categories[category.Id] = category.DefaultPolicy;
        }
    }

    public PortarrNotificationOptions Options { get; set; }

    public NotificationPolicyOptions Policy { get; set; } = new();

    public Dictionary<string, CategoryPolicy> Categories { get; } = new(StringComparer.Ordinal);

    public string? Problem { get; set; }

    public Task<NotificationSettingsState> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new NotificationSettingsState(Options, Policy, Categories));

    public Task<NotificationSettingsOutcome> UpdateAsync(NotificationSettingsChange change, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public string? Check(PortarrNotificationOptions options) => Problem;

    public string? CheckSecrets(PortarrNotificationOptions options) => null;
}

internal static class TestCategories
{
    public static NotificationCategories Declared()
    {
        var categories = new NotificationCategories();
        PortarrNotificationCategories.Declare(categories);
        return categories;
    }
}

internal static class Findings
{
    public static AttentionFinding BackupFailed(params string[] subjects) => new("data", AttentionLevel.Danger, "backup-failed", subjects.Length, subjects);

    public static AttentionFinding BackupMissing(params string[] subjects) => new("data", AttentionLevel.Danger, "data-missing", subjects.Length, subjects);

    public static AttentionFinding BackupLate(params string[] subjects) => new("data", AttentionLevel.Warning, "backup-late", subjects.Length, subjects);

    public static AttentionFinding AppDown(params string[] subjects) => new("apps", AttentionLevel.Danger, "app-down", subjects.Length, subjects);

    public static AttentionFinding AppDegraded(params string[] subjects) => new("apps", AttentionLevel.Warning, "app-degraded", subjects.Length, subjects);

    public static AttentionFinding AppUnknown(params string[] subjects) => new("apps", AttentionLevel.Info, "app-unknown", subjects.Length, subjects);

    public static AttentionFinding MachineNotReady(params string[] subjects) => new("infrastructure", AttentionLevel.Danger, "machine-not-ready", subjects.Length, subjects);

    public static AttentionFinding SevereAlerts(params string[] subjects) => new("maintenance", AttentionLevel.Danger, "severe-alerts", subjects.Length, subjects);

    public static AttentionFinding CiFailing(params string[] subjects) => new("maintenance", AttentionLevel.Warning, "ci-failing", subjects.Length, subjects);
}
