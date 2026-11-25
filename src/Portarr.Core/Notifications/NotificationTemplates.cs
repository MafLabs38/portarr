using System.Globalization;
using MafLabs.Notifications;
using Portarr.Core.Attention;

namespace Portarr.Core.Notifications;

/// <summary>
/// The mails of Portarr (ADR 0022): one HTML and plain-text template per category, written with the shared package's <see cref="MailTemplate"/> and
/// framed by its <see cref="MailLayout"/>. A mail lists the findings of one category, one line per thing concerned, with a sentence for the kind of
/// finding (a closed vocabulary; an unknown kind is written as its own code) and the names the catalog gave. The package cleans and encodes every value:
/// no control character, no markup, no header injected through a name.
/// </summary>
public static class NotificationTemplates
{
    /// <summary>The name of the program in the header band and the footer of every mail.</summary>
    public const string Product = "Portarr";

    private const string FooterTitle = "Notifications";

    private static readonly Dictionary<string, MailTemplate> Templates = new(StringComparer.Ordinal)
    {
        [PortarrNotificationCategories.Apps] = ForCategory("Applications need attention", "{{count}} app(s) need attention. Here is what Portarr sees right now:", "Apps"),
        [PortarrNotificationCategories.Infrastructure] = ForCategory("Infrastructure needs attention", "{{count}} item(s) of your infrastructure need attention. Here is what Portarr sees right now:", "Infrastructure"),
        [PortarrNotificationCategories.Data] = ForCategory("Backups need attention", "{{count}} data asset(s) need attention. Here is what Portarr sees right now:", "Data"),
        [PortarrNotificationCategories.Maintenance] = ForCategory("Maintenance needs attention", "{{count}} item(s) of maintenance need attention. Here is what Portarr sees right now:", "Maintenance"),
    };

    private static readonly MailTemplate Test = new(
        "Portarr: a test mail",
        "This mail was sent by Portarr to check that the notifications work. Nothing else is needed.",
        "Notifications work");

    /// <summary>What a kind of finding means, said as a sentence. A kind that is not here is written as its code.</summary>
    private static readonly Dictionary<string, string> Sentences = new(StringComparer.Ordinal)
    {
        ["app-down"] = "apps are down",
        ["app-degraded"] = "apps are degraded",
        ["app-unknown"] = "apps are in an unknown state",
        ["environment-unreachable"] = "environments cannot be reached",
        ["machine-not-ready"] = "machines are not ready",
        ["metrics-unavailable"] = "metrics cannot be read",
        ["backup-failed"] = "backups are failing",
        ["data-missing"] = "backups are missing",
        ["backup-late"] = "backups are late",
        ["backup-tool-not-set-up"] = "backup tools are not set up",
        ["critical-without-offsite"] = "critical data has no off-site copy",
        ["critical-offsite-pending"] = "off-site copies of critical data are pending",
        ["severe-alerts"] = "repositories have severe security alerts",
        ["secret-alerts"] = "repositories have exposed secrets",
        ["other-alerts"] = "repositories have other security alerts",
        ["ci-failing"] = "continuous integration is failing",
        ["image-major"] = "images are a major version behind",
        ["images-behind"] = "images are behind",
        ["image-rebuilt"] = "images have a newer build",
        ["images-unknown"] = "image versions cannot be checked",
        ["image-critical-vulnerabilities"] = "running images have critical vulnerabilities that have a fix",
        ["image-vulnerabilities"] = "running images have high vulnerabilities that have a fix",
        ["image-scanner-error"] = "the image scanner cannot be read",
        ["dependabot-off"] = "Dependabot is off",
        ["code-scanning-off"] = "code scanning is off",
        ["dependabot-prs"] = "Dependabot pull requests are waiting",
        ["code-source-error"] = "the code source cannot be read",
        ["platform-out-of-support"] = "platforms are out of support",
        ["platform-support-ending"] = "platform support is ending",
        ["platforms-behind"] = "platforms have a newer version",
        ["platform-lookup-failed"] = "platform support dates cannot be read",
    };

    /// <summary>The findings whose names are what a remote tool said: the sentence alone is written.</summary>
    private static readonly HashSet<string> WithoutNames = new(StringComparer.Ordinal) { "code-source-error", "image-scanner-error" };

    /// <summary>The test mail, to see that the settings work.</summary>
    public static RenderedMail RenderTest() =>
        Test.RenderMail(new MailContext(Product, FooterTitle, NotificationLevel.Info), new Dictionary<string, string>());

    /// <summary>
    /// The mail of a category: <paramref name="findings"/> are those it writes about, <paramref name="level"/> the worst of them.
    /// <paramref name="reminder"/> is 0 for the first mail of an episode, then the number of the reminder; <paramref name="willRemind"/> says whether
    /// Portarr will write again if nothing changes, <paramref name="reminderHours"/> when.
    /// </summary>
    public static RenderedMail Render(NotificationCategory category, IReadOnlyList<AttentionFinding> findings, NotificationLevel level, int reminder, int maxReminders, bool willRemind, int reminderHours)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(findings);
        var template = Templates.TryGetValue(category.Id, out var known) ? known : ForCategory($"{category.Title} needs attention", "{{count}} item(s) need attention. Here is what Portarr sees right now:", "Overview");
        var values = new Dictionary<string, string>
        {
            ["count"] = findings.Sum(f => f.Count).ToString(CultureInfo.InvariantCulture),
            ["stage"] = reminder == 0 ? "new" : $"reminder {reminder} of {maxReminders}",
            ["followUp"] = willRemind
                ? $"If nothing changes, Portarr reminds you again in {reminderHours} hour(s)."
                : "Portarr will not write again about this until it has gone and comes back.",
        };
        var lists = new Dictionary<string, IReadOnlyList<string>> { ["findings"] = Lines(findings) };
        return template.RenderMail(new MailContext(Product, category.Title, level, category.Id), values, lists);
    }

    /// <summary>The sentence that says what a kind of finding is.</summary>
    public static string SentenceOf(string kind) => Sentences.GetValueOrDefault(kind, kind.Replace('-', ' '));

    private static MailTemplate ForCategory(string title, string intro, string page) => new(
        "Portarr: " + title + " ({{stage}})",
        intro + "\n\n{{@findings}}\n\n> Open the " + page + " page of Portarr to see the details.\n\n{{followUp}}",
        title);

    /// <summary>One line per thing concerned: its level, the sentence of its kind and its name. A finding with no name to show is one line with its count.</summary>
    private static List<string> Lines(IReadOnlyList<AttentionFinding> findings)
    {
        var lines = new List<string>();
        foreach (var finding in findings)
        {
            var level = MailLayout.LabelOf(PortarrNotificationCategories.LevelOf(finding.Level));
            var sentence = SentenceOf(finding.Kind);
            var head = $"{level} - {char.ToUpperInvariant(sentence[0])}{sentence[1..]}";
            if (WithoutNames.Contains(finding.Kind) || finding.Subjects.Count == 0)
            {
                lines.Add($"{head} ({finding.Count})");
                continue;
            }

            lines.AddRange(finding.Subjects.Select(name => $"{head}: {name}"));
        }

        return lines;
    }
}
