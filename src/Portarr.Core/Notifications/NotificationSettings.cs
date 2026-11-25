using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using MafLabs.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;

namespace Portarr.Core.Notifications;

/// <summary>What the operator sets for one category (<see cref="CategoryPolicy"/> as the page sends it).</summary>
public sealed record NotificationCategoryChange(string Id, bool Enabled, NotificationLevel MinimumLevel, int ReminderHours, int MaxReminders, int MaxPerDay);

/// <summary>
/// What the operator may change from the page: whether Portarr writes, who is written to, from which address, how many mails in all a day, and what each
/// category does. The connection to the mail server (host, port, user, the file that holds the password) is deliberately not here: it is set where Portarr
/// is deployed, because whoever could change the host and keep the password file would make Portarr hand the password to a server of their choice.
/// </summary>
public sealed record NotificationSettingsChange(bool Enabled, string? Recipient, string? From, int MaxPerDay, IReadOnlyList<NotificationCategoryChange> Categories);

/// <summary>
/// The settings in force: Portarr's own options, the program-wide policy (with the operator's daily cap) and the policy of every declared category.
/// </summary>
public sealed record NotificationSettingsState(PortarrNotificationOptions Options, NotificationPolicyOptions Policy, IReadOnlyDictionary<string, CategoryPolicy> Categories);

/// <summary>
/// The result of a change: the settings now in force and, when the change was refused, what is wrong by field (<c>maxPerDay</c>,
/// <c>categories.data.reminderHours</c>, <c>settings</c>…); nothing was stored then.
/// </summary>
public sealed record NotificationSettingsOutcome(NotificationSettingsState State, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Saved => Errors.Count == 0;
}

public interface INotificationSettings
{
    /// <summary>The settings in force: the configuration, then what the operator saved from the page.</summary>
    Task<NotificationSettingsState> GetAsync(CancellationToken cancellationToken);

    /// <summary>Checks and stores the change, and applies it at once (the next mail uses it).</summary>
    Task<NotificationSettingsOutcome> UpdateAsync(NotificationSettingsChange change, CancellationToken cancellationToken);

    /// <summary>What is wrong with the settings (null when they are fine, or when notifications are off).</summary>
    string? Check(PortarrNotificationOptions options);

    /// <summary>What is wrong with the password file (null when there is none to read or it can be read). Never says what the password is.</summary>
    string? CheckSecrets(PortarrNotificationOptions options);
}

/// <summary>
/// The settings of the notifications, in force now. The configuration (<c>PORTARR__NOTIFICATIONS__*</c>) and the declared categories' defaults are the
/// base; what the operator saves from the page (<c>Config</c> table, key <c>Notifications</c>, non-secret values only) is laid over them. It is also the
/// <see cref="IOptions{TOptions}"/> the shared mail sender reads at each send, so a change needs no restart.
/// </summary>
public sealed partial class NotificationSettings : INotificationSettings, IOptions<NotificationOptions>
{
    public const string ConfigKey = "Notifications";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly PortarrNotificationOptions _configured;
    private readonly NotificationPolicyOptions _configuredPolicy;
    private readonly NotificationCategories _categories;
    private readonly IConfigRepository _repository;
    private readonly IEnumerable<INotificationProvider> _providers;
    private readonly PortarrNotificationOptionsValidator _validator;
    private readonly ILogger<NotificationSettings> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile NotificationSettingsState? _current;

    public NotificationSettings(
        IOptions<PortarrNotificationOptions> configured,
        IOptions<NotificationPolicyOptions> policy,
        NotificationCategories categories,
        IConfigRepository repository,
        IEnumerable<INotificationProvider> providers,
        ILogger<NotificationSettings> logger)
    {
        _configured = configured.Value;
        _configuredPolicy = policy.Value;
        _categories = categories;
        _repository = repository;
        _providers = providers;
        _validator = new PortarrNotificationOptionsValidator(providers);
        _logger = logger;
    }

    /// <summary>What the mail sender reads. Until the stored values are loaded it is the configuration alone; the callers that send load them first.</summary>
    NotificationOptions IOptions<NotificationOptions>.Value => _current?.Options ?? _configured;

    public async Task<NotificationSettingsState> GetAsync(CancellationToken cancellationToken)
    {
        if (_current is { } known)
        {
            return known;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _current ??= await LoadAsync(cancellationToken);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task<NotificationSettingsOutcome> UpdateAsync(NotificationSettingsChange change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = _current ??= await LoadAsync(cancellationToken);
            var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var categories = new Dictionary<string, CategoryPolicy>(current.Categories, StringComparer.Ordinal);
            ValidateCategories(change, categories, errors);
            if (new NotificationPolicyOptions { MaxPerDay = change.MaxPerDay }.Check() is { } capProblem)
            {
                Add(errors, "maxPerDay", capProblem);
            }

            var options = Lay(_configured, change);
            if (Check(options) is { } problem)
            {
                Add(errors, "settings", problem);
            }

            if (errors.Count > 0)
            {
                return new NotificationSettingsOutcome(current, errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
            }

            var candidate = new NotificationSettingsState(options, WithMaxPerDay(change.MaxPerDay), categories.ToFrozenDictionary(StringComparer.Ordinal));
            await _repository.SetAsync(ConfigKey, JsonSerializer.Serialize(ToStored(candidate), Json), cancellationToken);
            _current = candidate;
            return new NotificationSettingsOutcome(candidate, new Dictionary<string, string[]>());
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public string? Check(PortarrNotificationOptions options)
    {
        var result = _validator.Validate(null, options);
        return result.Failed ? result.FailureMessage : null;
    }

    public string? CheckSecrets(PortarrNotificationOptions options) =>
        NotificationProviders.Find(_providers, options.Provider)?.CheckSecrets(options);

    /// <summary>
    /// Puts the categories of the change in <paramref name="categories"/> and says what is wrong with them, by field. The package's validator says it in
    /// sentences; the field is the setting the sentence starts with (<c>Category 'data': ReminderHours is…</c>). A category the change does not name keeps what it has.
    /// </summary>
    private void ValidateCategories(NotificationSettingsChange change, Dictionary<string, CategoryPolicy> categories, Dictionary<string, List<string>> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in change.Categories)
        {
            if (!_categories.Contains(entry.Id))
            {
                Add(errors, "categories", $"There is no notification category '{Shorten(entry.Id)}'.");
                continue;
            }

            if (!seen.Add(entry.Id))
            {
                Add(errors, "categories", $"Category '{entry.Id}' is given twice.");
                continue;
            }

            var policy = new CategoryPolicy { Enabled = entry.Enabled, MinimumLevel = entry.MinimumLevel, ReminderHours = entry.ReminderHours, MaxReminders = entry.MaxReminders, MaxPerDay = entry.MaxPerDay };
            var sentences = CategoryPolicyValidator.Validate(entry.Id, policy).ToList();
            if (policy.MinimumLevel == NotificationLevel.Ok)
            {
                sentences.Add($"Category '{entry.Id}': MinimumLevel is info, warning or danger.");
            }

            foreach (var sentence in sentences)
            {
                Add(errors, $"categories.{entry.Id}.{FieldOf(sentence)}", sentence);
            }

            categories[entry.Id] = policy;
        }
    }

    private static string FieldOf(string sentence)
    {
        var start = sentence.IndexOf("': ", StringComparison.Ordinal) + 3;
        var end = sentence.IndexOf(' ', Math.Max(start, 0));
        var name = start >= 3 && end > start ? sentence[start..end] : "policy";
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string Shorten(string text) => text.Length > NotificationCategory.MaxIdLength ? text[..NotificationCategory.MaxIdLength] + "…" : text;

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            errors[key] = messages = [];
        }

        messages.Add(message);
    }

    private async Task<NotificationSettingsState> LoadAsync(CancellationToken cancellationToken)
    {
        var defaults = Categories(null);
        var baseline = new NotificationSettingsState(_configured, _configuredPolicy, defaults);
        var stored = await _repository.GetAsync(ConfigKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return baseline;
        }

        try
        {
            if (JsonSerializer.Deserialize<StoredSettings>(stored, Json) is not { } value)
            {
                return baseline;
            }

            var options = _configured.Copy();
            options.Enabled = value.Enabled;
            options.Recipient = Clean(value.Recipient);
            options.From = Clean(value.From);
            return new NotificationSettingsState(options, StoredCap(value.MaxPerDay), Categories(value.Categories));
        }
        catch (JsonException)
        {
            // A value edited by hand: the configuration stands, and the next save replaces it.
            LogUnreadable();
            return baseline;
        }
    }

    /// <summary>The policy of every declared category: the operator's, or the default of the category when there is none or it is corrupted (never a crash of the monitor).</summary>
    private FrozenDictionary<string, CategoryPolicy> Categories(IReadOnlyDictionary<string, CategoryPolicy>? operatorPolicies)
    {
        var policies = new Dictionary<string, CategoryPolicy>(StringComparer.Ordinal);
        foreach (var category in _categories.All)
        {
            try
            {
                policies[category.Id] = _categories.PolicyFor(category.Id, operatorPolicies);
            }
            catch (InvalidOperationException)
            {
                LogCorrupted(category.Id);
                policies[category.Id] = category.DefaultPolicy;
            }
        }

        return policies.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private NotificationPolicyOptions StoredCap(int? maxPerDay)
    {
        if (maxPerDay is not { } cap)
        {
            return _configuredPolicy;
        }

        if (new NotificationPolicyOptions { MaxPerDay = cap }.Check() is not null)
        {
            LogCapCorrupted();
            return _configuredPolicy;
        }

        return WithMaxPerDay(cap);
    }

    private NotificationPolicyOptions WithMaxPerDay(int maxPerDay) => new()
    {
        ConfirmMinutes = _configuredPolicy.ConfirmMinutes,
        ClearMinutes = _configuredPolicy.ClearMinutes,
        RetryMinutes = _configuredPolicy.RetryMinutes,
        MaxPerDay = maxPerDay,
    };

    private static PortarrNotificationOptions Lay(PortarrNotificationOptions configured, NotificationSettingsChange change)
    {
        var options = configured.Copy();
        options.Enabled = change.Enabled;
        options.Recipient = Clean(change.Recipient);
        options.From = Clean(change.From);
        return options;
    }

    private static StoredSettings ToStored(NotificationSettingsState state) =>
        new(state.Options.Enabled, state.Options.Recipient, state.Options.From, state.Policy.MaxPerDay, state.Categories.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal));

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>What is kept in the <c>Config</c> table: the operator's own, non-secret settings, and nothing of the connection.</summary>
    private sealed record StoredSettings(bool Enabled, string? Recipient, string? From, int? MaxPerDay, Dictionary<string, CategoryPolicy>? Categories);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification settings stored in the database cannot be read: the configuration is used")]
    private partial void LogUnreadable();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The stored policy of the notification category '{Category}' is not valid: its default is used")]
    private partial void LogCorrupted(string category);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The stored daily limit of notification mails is not valid: the configured one is used")]
    private partial void LogCapCorrupted();
}
