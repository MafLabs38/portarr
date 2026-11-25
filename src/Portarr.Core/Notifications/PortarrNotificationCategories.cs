using MafLabs.Notifications;
using Portarr.Core.Attention;
using Portarr.Core.Maintenance;

namespace Portarr.Core.Notifications;

/// <summary>
/// The categories of the mails Portarr writes (D-129, ADR 0022): one per area of what needs attention, each with its own text and its own settings.
/// A finding of another area (the catalog, the MCP servers) is not mailed. The defaults are what applies until the operator changes a category.
/// </summary>
public static class PortarrNotificationCategories
{
    public const string Apps = "apps";
    public const string Infrastructure = "infrastructure";
    public const string Data = "data";
    public const string Maintenance = "maintenance";

    /// <summary>
    /// Declares the categories, with their defaults. They are deliberately quiet, because little yet closes a problem once it is told: one mail, one
    /// reminder three days later, at most two mails a day per category. Apps, infrastructure and data are on: the first two from a critical finding,
    /// the backups from a warning (a late backup is worth a mail). Maintenance is OFF: its findings (vulnerabilities, outdated images, CI, Dependabot)
    /// are many and cannot be treated now, so even once it is turned on it writes one mail per episode, from critical, at most one a day, and no reminder.
    /// </summary>
    public static void Declare(NotificationCategories categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        _ = categories
            .Add(Apps, "Apps", "Apps that are down, degraded or unknown.", Quiet(enabled: true, NotificationLevel.Danger))
            .Add(Infrastructure, "Infrastructure", "Environments that cannot be reached, machines that are not ready.", Quiet(enabled: true, NotificationLevel.Danger))
            .Add(Data, "Data", "Backups that fail, are missing or late, and critical data without an off-site copy.", Quiet(enabled: true, NotificationLevel.Warning))
            .Add(Maintenance, "Maintenance", "Security alerts, outdated images, failing CI, Dependabot and platforms out of support.",
                new CategoryPolicy { Enabled = false, MinimumLevel = NotificationLevel.Danger, ReminderHours = 168, MaxReminders = 0, MaxPerDay = 1 });
    }

    /// <summary>One mail, one reminder after three days, at most two mails a day.</summary>
    private static CategoryPolicy Quiet(bool enabled, NotificationLevel minimum) =>
        new() { Enabled = enabled, MinimumLevel = minimum, ReminderHours = 72, MaxReminders = 1, MaxPerDay = 2 };

    /// <summary>The category that writes about a finding, or null when its area is not mailed.</summary>
    public static string? CategoryOf(AttentionFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return finding.Area switch
        {
            "apps" => Apps,
            "infrastructure" => Infrastructure,
            "data" => Data,
            "maintenance" => Maintenance,
            _ => null,
        };
    }

    /// <summary>The level of a finding as the package knows it.</summary>
    public static NotificationLevel LevelOf(AttentionLevel level) => level switch
    {
        AttentionLevel.Danger => NotificationLevel.Danger,
        AttentionLevel.Warning => NotificationLevel.Warning,
        _ => NotificationLevel.Info,
    };
}
