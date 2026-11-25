using FluentValidation;
using MafLabs.Notifications;
using Portarr.Core.Notifications;

namespace Portarr.Api.V1.Notifications;

/// <summary>
/// How Portarr writes to the operator (D-129). <c>Connection</c> is where the mail goes out, read-only here: it is set where Portarr is deployed
/// (<c>PORTARR__NOTIFICATIONS__SMTP__*</c>), and the password is a file, so the API says <em>whether</em> there is one, never what it holds nor where it is.
/// <c>Problem</c> is what is wrong with the settings when they are on (null when fine); <c>SecretProblem</c> what is wrong with the password file.
/// <c>LastAttempt</c> is the latest mail tried, delivered or not (null when none). <c>MaxPerDay</c> is the most mails in 24 hours, all categories
/// together; <c>Categories</c> are what each area of the attention findings does, in the order the pages show them.
/// </summary>
public sealed record NotificationSettingsResource(
    bool Enabled,
    string? Recipient,
    string? From,
    int MaxPerDay,
    NotificationConnectionResource Connection,
    string? Problem,
    string? SecretProblem,
    NotificationLogEntryResource? LastAttempt,
    IReadOnlyList<NotificationCategoryResource> Categories);

/// <summary>The connection to the mail server, as deployed. <c>PasswordFileConfigured</c> only says that a file is named.</summary>
public sealed record NotificationConnectionResource(string Provider, string? Host, int Port, string? UserName, bool StartTls, bool PasswordFileConfigured);

/// <summary>
/// A category of mails (<c>apps</c>, <c>infrastructure</c>, <c>data</c>, <c>maintenance</c>) and what it does now: written at all, from which level
/// (<c>info</c>, <c>warning</c> or <c>danger</c>), a reminder at most every <c>ReminderHours</c> and at most <c>MaxReminders</c> times while it lasts,
/// at most <c>MaxPerDay</c> mails in 24 hours. <c>Defaults</c> is what applies until the operator changes it.
/// </summary>
public sealed record NotificationCategoryResource(
    string Id,
    string Title,
    string Description,
    bool Enabled,
    string MinimumLevel,
    int ReminderHours,
    int MaxReminders,
    int MaxPerDay,
    NotificationCategoryPolicyResource Defaults);

/// <summary>The settings of a category with no identity: its default.</summary>
public sealed record NotificationCategoryPolicyResource(bool Enabled, string MinimumLevel, int ReminderHours, int MaxReminders, int MaxPerDay);

/// <summary>
/// What the operator changes: whether Portarr writes, to whom and from which address, at most <c>MaxPerDay</c> mails in 24 hours in all, and what each
/// category does. Every field is required. Turning it on needs the connection to be set. A category that is not listed keeps what it has.
/// </summary>
public sealed record NotificationSettingsUpdate(bool? Enabled, string? Recipient, string? From, int? MaxPerDay, IReadOnlyList<NotificationCategoryUpdate>? Categories);

/// <summary>One category's settings, as the page sends them. <c>MinimumLevel</c> is <c>info</c>, <c>warning</c> or <c>danger</c>.</summary>
public sealed record NotificationCategoryUpdate(string? Id, bool? Enabled, string? MinimumLevel, int? ReminderHours, int? MaxReminders, int? MaxPerDay);

/// <summary>The outcome of a test mail: sent, or why not (a sentence, never the server's text).</summary>
public sealed record NotificationTestResource(bool Ok, string? Error);

/// <summary>
/// One mail that was tried. <c>Kind</c> is <c>category</c> or <c>test</c>; <c>Category</c> the id of the category (null for a test); <c>Level</c> the level
/// of the mail (<c>info</c>, <c>warning</c>, <c>danger</c>); <c>Reminder</c> 0 for the first mail of an episode.
/// </summary>
public sealed record NotificationLogEntryResource(long Id, string Kind, string? Category, string? Level, int Reminder, DateTimeOffset SentAt, bool Delivered, string? Error);

public sealed record NotificationLogResource(IReadOnlyList<NotificationLogEntryResource> Entries);

/// <summary>
/// The shape of an update: what is missing or not understood, by field (<c>enabled</c>, <c>categories.data.minimumLevel</c>…). What is out of range is
/// the package's to say, when the settings are checked.
/// </summary>
public sealed class NotificationSettingsUpdateValidator : AbstractValidator<NotificationSettingsUpdate>
{
    private static readonly string[] Levels = ["info", "warning", "danger"];

    public NotificationSettingsUpdateValidator()
    {
        RuleFor(s => s).Custom((update, context) =>
        {
            foreach (var (key, message) in Problems(update))
            {
                context.AddFailure(key, message);
            }
        });
    }

    public static IEnumerable<(string Key, string Message)> Problems(NotificationSettingsUpdate update)
    {
        if (update.Enabled is null)
        {
            yield return ("enabled", "enabled is required.");
        }

        if (!string.IsNullOrWhiteSpace(update.Recipient) && !IsAddress(update.Recipient))
        {
            yield return ("recipient", "recipient must be a mail address.");
        }

        if (!string.IsNullOrWhiteSpace(update.From) && !IsAddress(update.From))
        {
            yield return ("from", "from must be a mail address.");
        }

        if (update.MaxPerDay is null)
        {
            yield return ("maxPerDay", "maxPerDay is required.");
        }

        if (update.Categories is null)
        {
            yield return ("categories", "categories is required.");
            yield break;
        }

        for (var i = 0; i < update.Categories.Count; i++)
        {
            foreach (var problem in CategoryProblems(update.Categories[i], i))
            {
                yield return problem;
            }
        }
    }

    private static IEnumerable<(string Key, string Message)> CategoryProblems(NotificationCategoryUpdate? category, int index)
    {
        if (category is null || string.IsNullOrWhiteSpace(category.Id))
        {
            yield return ($"categories[{index}].id", "id is required.");
            yield break;
        }

        var prefix = $"categories.{category.Id}";
        if (category.Enabled is null)
        {
            yield return ($"{prefix}.enabled", "enabled is required.");
        }

        if (category.MinimumLevel is null)
        {
            yield return ($"{prefix}.minimumLevel", "minimumLevel is required.");
        }
        else if (!Levels.Contains(category.MinimumLevel, StringComparer.OrdinalIgnoreCase))
        {
            yield return ($"{prefix}.minimumLevel", "minimumLevel is info, warning or danger.");
        }

        if (category.ReminderHours is null)
        {
            yield return ($"{prefix}.reminderHours", "reminderHours is required.");
        }

        if (category.MaxReminders is null)
        {
            yield return ($"{prefix}.maxReminders", "maxReminders is required.");
        }

        if (category.MaxPerDay is null)
        {
            yield return ($"{prefix}.maxPerDay", "maxPerDay is required.");
        }
    }

    private static bool IsAddress(string text) =>
        text.All(character => !char.IsControl(character)) && global::System.Net.Mail.MailAddress.TryCreate(text, out var address) && address.Address == text.Trim();
}

public static class NotificationResourceMapper
{
    public static NotificationLogEntryResource ToResource(this NotificationLogEntry entry) =>
        new(entry.Id, entry.Kind, entry.Category, entry.Level, entry.Reminder, entry.SentAt, entry.Delivered, entry.Error);

    public static NotificationCategoryResource ToResource(this NotificationCategory category, CategoryPolicy policy) =>
        new(category.Id, category.Title, category.Description, policy.Enabled, LevelText(policy.MinimumLevel), policy.ReminderHours, policy.MaxReminders, policy.MaxPerDay, category.DefaultPolicy.ToResource());

    public static NotificationCategoryPolicyResource ToResource(this CategoryPolicy policy) =>
        new(policy.Enabled, LevelText(policy.MinimumLevel), policy.ReminderHours, policy.MaxReminders, policy.MaxPerDay);

    /// <summary>The change the page asks for. The update has been checked for its shape: every field is there and the level is one of the three.</summary>
    public static NotificationSettingsChange ToChange(this NotificationSettingsUpdate update) =>
        new(
            update.Enabled!.Value,
            update.Recipient,
            update.From,
            update.MaxPerDay!.Value,
            [.. update.Categories!.Select(c => new NotificationCategoryChange(c!.Id!, c.Enabled!.Value, Enum.Parse<NotificationLevel>(c.MinimumLevel!, ignoreCase: true), c.ReminderHours!.Value, c.MaxReminders!.Value, c.MaxPerDay!.Value))]);

    private static string LevelText(NotificationLevel level) => level.ToString().ToLowerInvariant();
}
