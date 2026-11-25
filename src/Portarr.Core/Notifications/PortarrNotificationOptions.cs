using MafLabs.Notifications;

namespace Portarr.Core.Notifications;

/// <summary>
/// The settings of the notifications that are Portarr's own (D-129, ADR 0022), section <c>Notifications</c> (<c>PORTARR__NOTIFICATIONS__*</c>): the shared
/// package's settings (recipient, sender, SMTP connection, password file) and how often Portarr looks. Off by default: turning it on lets Portarr write to
/// ONE explicit recipient, with a text of its own made from closed codes and the names the catalog gave, never what a remote tool sent. The delays and the
/// daily cap of all categories are the package's <see cref="NotificationPolicyOptions"/> (<c>PORTARR__NOTIFICATIONS__POLICY__*</c>); what each category
/// does is a <see cref="CategoryPolicy"/> the operator edits.
/// </summary>
public sealed class PortarrNotificationOptions : NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>The fewest seconds between two looks at what needs attention.</summary>
    public const int MinPassSeconds = 10;

    /// <summary>The most seconds between two looks at what needs attention.</summary>
    public const int MaxPassSeconds = 3600;

    /// <summary>Seconds between two looks at what needs attention (10 to 3600).</summary>
    public int PassSeconds { get; set; } = 60;

    internal PortarrNotificationOptions Copy() => new()
    {
        Enabled = Enabled,
        Provider = Provider,
        Recipient = Recipient,
        From = From,
        Smtp = new SmtpOptions
        {
            Host = Smtp.Host,
            Port = Smtp.Port,
            UserName = Smtp.UserName,
            PasswordFile = Smtp.PasswordFile,
            StartTls = Smtp.StartTls,
            TimeoutSeconds = Smtp.TimeoutSeconds,
        },
        PassSeconds = PassSeconds,
    };
}

/// <summary>Checks Portarr's own setting on top of the package's check of the recipient and the connection.</summary>
public sealed class PortarrNotificationOptionsValidator : NotificationOptionsValidator
{
    public PortarrNotificationOptionsValidator(IEnumerable<INotificationProvider> providers)
        : base(providers)
    {
    }

    protected override string? ValidatePolicy(NotificationOptions options) =>
        options is PortarrNotificationOptions { PassSeconds: < PortarrNotificationOptions.MinPassSeconds or > PortarrNotificationOptions.MaxPassSeconds }
            ? $"Notifications:PassSeconds is the seconds between two looks, from {PortarrNotificationOptions.MinPassSeconds} to {PortarrNotificationOptions.MaxPassSeconds}."
            : null;
}
