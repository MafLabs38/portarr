using MafLabs.Notifications;
using Microsoft.Extensions.Logging;
using Portarr.Core.Attention;

namespace Portarr.Core.Notifications;

/// <summary>
/// Tells the operator that something needs them (ADR 0022). Each pass reads what <see cref="IAttentionService"/> finds, sorts the findings into the
/// categories (<see cref="PortarrNotificationCategories"/>) and keeps ONE episode per category: the findings at or above the category's minimum level.
/// The shared package's <see cref="NotificationDecider"/> says when a mail is due (once the category has had findings for the confirmation delay,
/// then reminders), and the mail lists everything the category has at that moment. What is stored (the episodes, the log of attempts) means a
/// restart sends nothing twice, and the two daily caps (the category's, all categories) hold whatever the tools report. Only the configured recipient
/// is ever written to. When the findings of a category change while its episode lasts, nothing extra is sent: the next reminder lists the new set.
/// </summary>
public sealed partial class AttentionNotifier
{
    private static readonly TimeSpan KeepLog = TimeSpan.FromDays(30);
    private const int MaxReason = 200;

    private readonly IAttentionService _attention;
    private readonly INotificationSettings _settings;
    private readonly NotificationCategories _categories;
    private readonly INotificationSender _sender;
    private readonly INotificationStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AttentionNotifier> _logger;
    private readonly SemaphoreSlim _one = new(1, 1);
    private DateTimeOffset? _runStartedAt;
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;
    private bool _problemTold;

    public AttentionNotifier(IAttentionService attention, INotificationSettings settings, NotificationCategories categories, INotificationSender sender, INotificationStore store, TimeProvider timeProvider, ILogger<AttentionNotifier> logger)
    {
        _attention = attention;
        _settings = settings;
        _categories = categories;
        _sender = sender;
        _store = store;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>One pass: the mails that are due. Returns how many left.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        _runStartedAt ??= _timeProvider.GetUtcNow();
        var state = await _settings.GetAsync(cancellationToken);
        if (!state.Options.Enabled)
        {
            return 0;
        }

        if (_settings.Check(state.Options) is { } problem)
        {
            // Told once, not every minute: the settings page shows it.
            if (!_problemTold)
            {
                LogMisconfigured(problem);
                _problemTold = true;
            }

            return 0;
        }

        _problemTold = false;

        // One look at the caps and one send at a time, whoever asks (the pass, a test mail): the caps are read, then the attempt is written.
        await _one.WaitAsync(cancellationToken);
        try
        {
            return await PassAsync(state, cancellationToken);
        }
        finally
        {
            _ = _one.Release();
        }
    }

    /// <summary>A mail to see whether the settings work. It counts in the daily cap of all mails, and says why when it fails.</summary>
    public async Task<NotificationResult> SendTestAsync(CancellationToken cancellationToken)
    {
        var state = await _settings.GetAsync(cancellationToken);
        if (!state.Options.Enabled)
        {
            return NotificationResult.Failed("Notifications are off.");
        }

        if (_settings.Check(state.Options) is { } problem)
        {
            return NotificationResult.Failed(problem);
        }

        await _one.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (await _store.CountSinceAsync(now - NotificationDecider.Day, null, cancellationToken) >= state.Policy.MaxPerDay)
            {
                return NotificationResult.Failed("The daily limit of mails is reached.");
            }

            var mail = NotificationTemplates.RenderTest();
            var result = await SendAsync(mail.ToNotification(state.Options.Recipient!), cancellationToken);
            await _store.RecordAsync(Entry(NotificationLogKinds.Test, null, null, NotificationLevel.Info, 0, now, result), cancellationToken);
            return result;
        }
        finally
        {
            _ = _one.Release();
        }
    }

    private async Task<int> PassAsync(NotificationSettingsState state, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var qualifying = Group(_attention.GetFindings(), state);
        var episodes = (await _store.ReadEpisodesAsync(cancellationToken)).ToDictionary(e => e.Key, StringComparer.Ordinal);
        var today = await _store.CountSinceAsync(now - NotificationDecider.Day, null, cancellationToken);
        var sent = 0;

        foreach (var category in _categories.All)
        {
            if (!qualifying.TryGetValue(category.Id, out var findings))
            {
                continue;
            }

            var episode = episodes.TryGetValue(category.Id, out var known) ? known with { LastSeenAt = now } : new NotificationEpisode(category.Id, now, now, 0, null, null);
            var policy = state.Categories[category.Id];
            var level = findings.Max(f => PortarrNotificationCategories.LevelOf(f.Level));
            var inCategory = await _store.CountSinceAsync(now - NotificationDecider.Day, category.Id, cancellationToken);
            var decision = NotificationDecider.Decide(now, level, policy, episode.ToState(), new SentToday(inCategory, today), state.Policy);
            if (decision.Send)
            {
                var result = await SendCategoryAsync(state.Options.Recipient!, new CategoryMail(category, episode, findings, level, policy), now, cancellationToken);
                today++;
                episode = result.Delivered
                    ? episode with { Mails = episode.Mails + 1, LastMailedAt = now, LastAttemptAt = now }
                    : episode with { LastAttemptAt = now };
                if (result.Delivered)
                {
                    sent++;
                }
            }

            await _store.SaveEpisodeAsync(episode, cancellationToken);
        }

        var runStartedAt = _runStartedAt!.Value;
        foreach (var over in episodes.Values.Where(e => !qualifying.ContainsKey(e.Key) && NotificationDecider.HasEnded(e.ToState(), state.Policy, now, runStartedAt)))
        {
            await _store.DeleteEpisodeAsync(over.Key, cancellationToken);
        }

        await PruneAsync(now, cancellationToken);
        return sent;
    }

    /// <summary>
    /// The findings each category writes about: those of its area at or above its minimum level, none when the category is off. A finding whose area
    /// is not a category (the catalog, the MCP servers) is not mailed.
    /// </summary>
    private static Dictionary<string, List<AttentionFinding>> Group(IReadOnlyList<AttentionFinding> findings, NotificationSettingsState state)
    {
        var grouped = new Dictionary<string, List<AttentionFinding>>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            if (PortarrNotificationCategories.CategoryOf(finding) is not { } id
                || !state.Categories.TryGetValue(id, out var policy)
                || !policy.Enabled
                || PortarrNotificationCategories.LevelOf(finding.Level) < policy.MinimumLevel)
            {
                continue;
            }

            if (!grouped.TryGetValue(id, out var group))
            {
                grouped[id] = group = [];
            }

            group.Add(finding);
        }

        return grouped;
    }

    private async Task<NotificationResult> SendCategoryAsync(string recipient, CategoryMail mail, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (category, episode, findings, level, policy) = mail;
        var rendered = NotificationTemplates.Render(category, findings, level, episode.Mails, policy.MaxReminders, NotificationDecider.WillRemind(episode.ToState(), policy), policy.ReminderHours);
        var result = await SendAsync(rendered.ToNotification(recipient), cancellationToken);
        await _store.RecordAsync(Entry(NotificationLogKinds.Category, category.Id, episode.Name, level, episode.Mails, now, result), cancellationToken);
        return result;
    }

    /// <summary>A sender is asked not to throw, but one that does is a failed attempt like any other: it is recorded, so that the caps and the pause hold.</summary>
    private async Task<NotificationResult> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            return await _sender.SendAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return NotificationResult.Failed("The mail could not be sent.");
        }
    }

    private static NotificationLogEntry Entry(string kind, string? category, string? episodeName, NotificationLevel level, int reminder, DateTimeOffset at, NotificationResult result)
    {
        var reason = result.Reason is { } text && text.Length > MaxReason ? text[..MaxReason] : result.Reason;
        return new NotificationLogEntry(0, kind, category, episodeName, level.ToString().ToLowerInvariant(), reminder, at, result.Delivered, result.Delivered ? null : reason ?? "The mail could not be sent.");
    }

    private async Task PruneAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now - _lastPrune < NotificationDecider.Day)
        {
            return;
        }

        await _store.PruneAsync(now - KeepLog, cancellationToken);
        _lastPrune = now;
    }

    /// <summary>What one mail is made of: the category, its episode, the findings it lists, the worst level among them and the category's policy.</summary>
    private sealed record CategoryMail(NotificationCategory Category, NotificationEpisode Episode, List<AttentionFinding> Findings, NotificationLevel Level, CategoryPolicy Policy);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notifications are on but cannot work: {Problem}")]
    private partial void LogMisconfigured(string problem);
}
