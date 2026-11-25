using MafLabs.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Attention;
using Portarr.Core.Maintenance;
using Portarr.Core.Notifications;

namespace Portarr.Core.Test.Notifications;

public sealed class AttentionNotifierFixture
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FakeNotificationStore _store = new();
    private readonly IAttentionService _attention = Substitute.For<IAttentionService>();
    private readonly INotificationSender _sender = Substitute.For<INotificationSender>();
    private readonly List<Notification> _sent = [];
    private readonly FakeNotificationSettings _settings;
    private bool _delivered = true;

    public AttentionNotifierFixture()
    {
        _settings = new FakeNotificationSettings(new PortarrNotificationOptions { Enabled = true, Recipient = "operator@homelab.example", From = "portarr@homelab.example" });
        _attention.GetFindings().Returns([]);
        _sender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _sent.Add(call.Arg<Notification>());
            return Task.FromResult(_delivered ? NotificationResult.Done : NotificationResult.Failed("The mail server could not be reached."));
        });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static TimeSpan Confirm => TimeSpan.FromMinutes(new NotificationPolicyOptions().ConfirmMinutes);

    private AttentionNotifier Notifier => new(_attention, _settings, TestCategories.Declared(), _sender, _store, _clock, NullLogger<AttentionNotifier>.Instance);

    private void Given(params AttentionFinding[] findings) => _attention.GetFindings().Returns(findings);

    /// <summary>Runs a pass now, and again after the confirmation delay: the first one sees the problem, the second finds it has lasted.</summary>
    private async Task<int> PassAfterConfirmationAsync(AttentionNotifier notifier)
    {
        _ = await notifier.RunOnceAsync(Token);
        _clock.Advance(Confirm);
        return await notifier.RunOnceAsync(Token);
    }

    [Fact]
    public async Task should_send_nothing_when_notifications_are_off()
    {
        _settings.Options.Enabled = false;
        Given(Findings.AppDown("wiki"));

        var sent = await PassAfterConfirmationAsync(Notifier);

        sent.Should().Be(0);
        _sent.Should().BeEmpty();
        _store.Episodes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_send_nothing_when_the_settings_cannot_work()
    {
        _settings.Problem = "Notifications:Recipient is needed.";
        Given(Findings.AppDown("wiki"));

        (await PassAfterConfirmationAsync(Notifier)).Should().Be(0);

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_send_one_html_and_text_mail_when_the_category_has_had_a_finding_for_the_confirmation_delay()
    {
        Given(Findings.AppDown("wiki (down)"));
        var notifier = Notifier;

        (await notifier.RunOnceAsync(Token)).Should().Be(0, "a finding seen for the first time may be a blip");
        _clock.Advance(Confirm);
        (await notifier.RunOnceAsync(Token)).Should().Be(1);

        var mail = _sent.Should().ContainSingle().Subject;
        mail.Recipient.Should().Be("operator@homelab.example");
        mail.Subject.Should().Be("Portarr: Applications need attention (new)");
        mail.Body.Should().Contain("wiki (down)");
        mail.Content!.Blocks.OfType<MessageItems>().Single().Lines.Should().Contain(line => line.Contains("wiki (down)"));
        mail.Body.Should().NotContain("-- ", "the text is bare: the mail provider adds its own signature");
        mail.Level.Should().Be(NotificationLevel.Danger);
        mail.Category.Should().Be("Apps");
        _store.Log.Should().ContainSingle().Which.Should().Match<NotificationLogEntry>(e => e.Delivered && e.Kind == "category" && e.Category == "apps" && e.Level == "danger" && e.Reminder == 0);
    }

    [Fact]
    public async Task should_send_one_mail_for_a_category_with_three_findings()
    {
        Given(Findings.BackupFailed("media (critical)"), Findings.BackupLate("photos (important)"), Findings.BackupMissing("notes (normal)"));

        (await PassAfterConfirmationAsync(Notifier)).Should().Be(1);

        var mail = _sent.Should().ContainSingle().Subject;
        mail.Subject.Should().Be("Portarr: Backups need attention (new)");
        mail.Body.Should().Contain("media (critical)").And.Contain("photos (important)").And.Contain("notes (normal)");
        mail.Level.Should().Be(NotificationLevel.Danger, "the level of the mail is the worst finding's");
        _store.Log.Should().ContainSingle().Which.Category.Should().Be("data");
        _store.Episodes.Keys.Should().Equal("data");
    }

    [Fact]
    public async Task should_send_one_mail_per_category_when_two_categories_have_findings()
    {
        Given(Findings.AppDown("wiki"), Findings.MachineNotReady("server-1 (lab)"), Findings.BackupFailed("media"));

        (await PassAfterConfirmationAsync(Notifier)).Should().Be(3);

        _sent.Select(m => m.Subject).Should().BeEquivalentTo(
            "Portarr: Applications need attention (new)",
            "Portarr: Infrastructure needs attention (new)",
            "Portarr: Backups need attention (new)");
        _store.Log.Select(e => e.Category).Should().BeEquivalentTo("apps", "infrastructure", "data");
    }

    [Fact]
    public async Task should_send_nothing_about_maintenance_by_default()
    {
        Given(Findings.SevereAlerts("org/repo"), Findings.CiFailing("org/repo"));

        (await PassAfterConfirmationAsync(Notifier)).Should().Be(0);

        _sent.Should().BeEmpty();
        _store.Episodes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_write_about_maintenance_once_the_operator_turns_it_on()
    {
        _settings.Categories["maintenance"] = _settings.Categories["maintenance"] with { Enabled = true };
        Given(Findings.SevereAlerts("org/repo"), Findings.CiFailing("org/other"));

        (await PassAfterConfirmationAsync(Notifier)).Should().Be(1);

        var mail = _sent.Should().ContainSingle().Subject;
        mail.Subject.Should().Be("Portarr: Maintenance needs attention (new)");
        mail.Body.Should().Contain("org/repo");
        mail.Body.Should().NotContain("org/other", "a warning is under the minimum level of maintenance (danger)");
    }

    [Fact]
    public async Task should_not_write_about_a_finding_under_the_minimum_level_of_its_category_nor_about_other_areas()
    {
        Given(Findings.AppDegraded("wiki"), Findings.AppUnknown("files"), new AttentionFinding("catalog", AttentionLevel.Danger, "catalog-errors", 2, []), new AttentionFinding("mcp", AttentionLevel.Danger, "mcp-server-down", 1, ["tools"]));

        (await PassAfterConfirmationAsync(Notifier)).Should().Be(0);

        _sent.Should().BeEmpty();
        _store.Episodes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_write_about_a_warning_when_the_category_minimum_level_is_lowered()
    {
        _settings.Categories["apps"] = _settings.Categories["apps"] with { MinimumLevel = NotificationLevel.Warning };
        Given(Findings.AppDegraded("wiki"), Findings.AppUnknown("files"));

        (await PassAfterConfirmationAsync(Notifier)).Should().Be(1);

        var mail = _sent.Should().ContainSingle().Subject;
        mail.Level.Should().Be(NotificationLevel.Warning);
        mail.Body.Should().Contain("wiki").And.NotContain("files");
    }

    [Fact]
    public async Task should_write_about_a_late_backup_by_default_because_the_data_category_starts_at_a_warning()
    {
        Given(Findings.BackupLate("photos (important)"));

        (await PassAfterConfirmationAsync(Notifier)).Should().Be(1);

        _sent.Single().Level.Should().Be(NotificationLevel.Warning);
    }

    [Fact]
    public async Task should_write_nothing_for_a_category_the_operator_turned_off()
    {
        _settings.Categories["apps"] = _settings.Categories["apps"] with { Enabled = false };
        Given(Findings.AppDown("wiki"), Findings.BackupFailed("media"));

        (await PassAfterConfirmationAsync(Notifier)).Should().Be(1);

        _sent.Single().Subject.Should().Contain("Backups");
    }

    [Fact]
    public async Task should_send_nothing_when_the_finding_goes_before_it_was_confirmed()
    {
        var notifier = Notifier;
        Given(Findings.AppDown("wiki"));
        _ = await notifier.RunOnceAsync(Token);
        _clock.Advance(TimeSpan.FromMinutes(3));
        Given();
        _ = await notifier.RunOnceAsync(Token);
        _clock.Advance(TimeSpan.FromMinutes(3));
        Given(Findings.AppDown("wiki"));

        (await notifier.RunOnceAsync(Token)).Should().Be(0, "the finding came back: it is a new episode that starts counting again");

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_write_again_the_same_day_when_the_category_lasts()
    {
        Given(Findings.AppDown("wiki"));
        var notifier = Notifier;
        _ = await PassAfterConfirmationAsync(notifier);

        for (var i = 0; i < 20; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(60));
            _ = await notifier.RunOnceAsync(Token);
        }

        _sent.Should().ContainSingle("20 hours later it is still the same mail");
    }

    [Fact]
    public async Task should_remind_after_a_day_and_at_most_three_times()
    {
        _settings.Categories["apps"] = _settings.Categories["apps"] with { ReminderHours = 24, MaxReminders = 3, MaxPerDay = 3 };
        Given(Findings.AppDown("wiki"));
        var notifier = Notifier;
        _ = await PassAfterConfirmationAsync(notifier);

        for (var day = 0; day < 8; day++)
        {
            _clock.Advance(TimeSpan.FromHours(24));
            _ = await notifier.RunOnceAsync(Token);
        }

        _sent.Should().HaveCount(4, "one mail and three reminders");
        _sent[1].Subject.Should().Contain("reminder 1 of 3");
        _sent[3].Subject.Should().Contain("reminder 3 of 3");
        _sent[3].Body.Should().Contain("will not write again");
        _store.Log.Select(e => e.Reminder).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public async Task should_write_one_mail_and_one_reminder_after_three_days_by_default_then_stay_silent()
    {
        Given(Findings.AppDown("wiki"));
        var notifier = Notifier;
        _ = await PassAfterConfirmationAsync(notifier);

        for (var hours = 0; hours < 24 * 10; hours += 12)
        {
            _clock.Advance(TimeSpan.FromHours(12));
            _ = await notifier.RunOnceAsync(Token);
        }

        _sent.Should().HaveCount(2, "one mail and one reminder, then nothing while it lasts");
        _sent[1].Subject.Should().Contain("reminder 1 of 1");
    }

    [Fact]
    public async Task should_follow_the_reminder_settings_of_the_category()
    {
        _settings.Categories["data"] = _settings.Categories["data"] with { ReminderHours = 2, MaxReminders = 1, MaxPerDay = 10 };
        Given(Findings.BackupFailed("media"));
        var notifier = Notifier;
        _ = await PassAfterConfirmationAsync(notifier);

        for (var hour = 0; hour < 10; hour++)
        {
            _clock.Advance(TimeSpan.FromHours(1));
            _ = await notifier.RunOnceAsync(Token);
        }

        _sent.Should().HaveCount(2, "one mail and one reminder, two hours later");
        _sent[1].Subject.Should().Contain("reminder 1 of 1");
    }

    [Fact]
    public async Task should_list_the_current_findings_in_a_reminder_but_send_nothing_extra_when_the_set_changes_in_the_meantime()
    {
        _settings.Categories["data"] = _settings.Categories["data"] with { ReminderHours = 24, MaxReminders = 3, MaxPerDay = 3 };
        Given(Findings.BackupFailed("media"));
        var notifier = Notifier;
        _ = await PassAfterConfirmationAsync(notifier);

        Given(Findings.BackupFailed("media"), Findings.BackupLate("photos"));
        _clock.Advance(TimeSpan.FromHours(1));
        (await notifier.RunOnceAsync(Token)).Should().Be(0, "the episode of the category goes on: nothing extra");
        _clock.Advance(TimeSpan.FromHours(24));
        (await notifier.RunOnceAsync(Token)).Should().Be(1);

        _sent.Should().HaveCount(2);
        _sent[0].Body.Should().NotContain("photos");
        _sent[1].Body.Should().Contain("media").And.Contain("photos");
    }

    [Fact]
    public async Task should_send_nothing_twice_when_portarr_restarts_in_the_middle_of_an_episode()
    {
        Given(Findings.AppDown("wiki"));
        _ = await PassAfterConfirmationAsync(Notifier);
        _clock.Advance(TimeSpan.FromHours(2));

        var restarted = Notifier;
        _ = await restarted.RunOnceAsync(Token);
        _clock.Advance(TimeSpan.FromMinutes(10));
        _ = await restarted.RunOnceAsync(Token);

        _sent.Should().ContainSingle();
    }

    [Fact]
    public async Task should_send_nothing_when_the_category_clears_and_forget_the_episode_after_the_clear_delay()
    {
        Given(Findings.AppDown("wiki"));
        var notifier = Notifier;
        _ = await PassAfterConfirmationAsync(notifier);
        Given();

        _clock.Advance(TimeSpan.FromMinutes(14));
        _ = await notifier.RunOnceAsync(Token);
        _store.Episodes.Should().ContainKey("apps", "a tool that is slow to answer must not end the episode");
        _clock.Advance(TimeSpan.FromMinutes(1));
        _ = await notifier.RunOnceAsync(Token);

        _store.Episodes.Should().BeEmpty();
        _sent.Should().ContainSingle("nothing is written when it ends");
    }

    [Fact]
    public async Task should_end_the_episode_when_the_operator_turns_the_category_off_and_start_a_new_one_when_it_is_back_on()
    {
        Given(Findings.AppDown("wiki"));
        var notifier = Notifier;
        _ = await PassAfterConfirmationAsync(notifier);
        _settings.Categories["apps"] = _settings.Categories["apps"] with { Enabled = false };
        _clock.Advance(TimeSpan.FromMinutes(20));
        _ = await notifier.RunOnceAsync(Token);
        _store.Episodes.Should().BeEmpty();
        _settings.Categories["apps"] = _settings.Categories["apps"] with { Enabled = true };

        _ = await PassAfterConfirmationAsync(notifier);

        _sent.Should().HaveCount(2);
        _sent[1].Subject.Should().Contain("(new)");
    }

    [Fact]
    public async Task should_start_a_new_episode_and_write_again_when_the_category_comes_back_after_it_ended()
    {
        Given(Findings.AppDown("wiki"));
        var notifier = Notifier;
        _ = await PassAfterConfirmationAsync(notifier);
        Given();
        _clock.Advance(TimeSpan.FromMinutes(20));
        _ = await notifier.RunOnceAsync(Token);
        _clock.Advance(TimeSpan.FromHours(1));
        Given(Findings.AppDown("wiki"));

        _ = await PassAfterConfirmationAsync(notifier);

        _sent.Should().HaveCount(2);
        _sent[1].Subject.Should().Contain("(new)");
    }

    [Fact]
    public async Task should_wait_for_the_retry_delay_and_not_count_a_mail_that_did_not_leave()
    {
        _delivered = false;
        Given(Findings.AppDown("wiki"));
        var notifier = Notifier;
        _ = await PassAfterConfirmationAsync(notifier);
        _clock.Advance(TimeSpan.FromMinutes(14));
        _ = await notifier.RunOnceAsync(Token);
        _sent.Should().ContainSingle("the retry delay is 15 minutes");

        _delivered = true;
        _clock.Advance(TimeSpan.FromMinutes(1));
        (await notifier.RunOnceAsync(Token)).Should().Be(1);

        _sent.Should().HaveCount(2);
        _store.Episodes["apps"].Mails.Should().Be(1);
        _store.Log.Select(e => (e.Delivered, e.Error)).Should().Equal((false, "The mail server could not be reached."), (true, null));
    }

    [Fact]
    public async Task should_record_a_failed_attempt_when_the_sender_throws()
    {
        _sender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns<Task<NotificationResult>>(_ => throw new InvalidOperationException("secret detail"));
        Given(Findings.AppDown("wiki"));

        (await PassAfterConfirmationAsync(Notifier)).Should().Be(0);

        _store.Log.Should().ContainSingle().Which.Error.Should().Be("The mail could not be sent.");
    }

    [Fact]
    public async Task should_stop_at_the_overall_daily_limit_whatever_the_tools_report()
    {
        _settings.Policy.MaxPerDay = 2;
        Given(Findings.AppDown("wiki"), Findings.MachineNotReady("server-1"), Findings.BackupFailed("media"));

        var sent = await PassAfterConfirmationAsync(Notifier);

        sent.Should().Be(2);
        _sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_stop_at_the_daily_limit_of_one_category_when_it_flaps()
    {
        _settings.Categories["apps"] = _settings.Categories["apps"] with { MaxReminders = 30, ReminderHours = 1, MaxPerDay = 2 };
        Given(Findings.AppDown("wiki"));
        var notifier = Notifier;
        _ = await PassAfterConfirmationAsync(notifier);

        for (var hour = 0; hour < 10; hour++)
        {
            _clock.Advance(TimeSpan.FromHours(1));
            _ = await notifier.RunOnceAsync(Token);
        }

        _sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_send_a_test_mail_in_html_and_count_it_in_the_daily_limit()
    {
        var result = await Notifier.SendTestAsync(Token);

        result.Delivered.Should().BeTrue();
        var mail = _sent.Should().ContainSingle().Subject;
        mail.Subject.Should().Be("Portarr: a test mail");
        mail.Content.Should().NotBeNull();
        _store.Log.Should().ContainSingle().Which.Should().Match<NotificationLogEntry>(e => e.Kind == "test" && e.Category == null && e.Level == "info");

        _settings.Policy.MaxPerDay = 1;
        (await Notifier.SendTestAsync(Token)).Reason.Should().Be("The daily limit of mails is reached.");
    }

    [Fact]
    public async Task should_say_why_the_test_mail_cannot_be_sent_when_notifications_are_off_or_misconfigured()
    {
        _settings.Options.Enabled = false;
        (await Notifier.SendTestAsync(Token)).Reason.Should().Be("Notifications are off.");

        _settings.Options.Enabled = true;
        _settings.Problem = "Notifications:Smtp:Host is needed.";
        (await Notifier.SendTestAsync(Token)).Reason.Should().Be("Notifications:Smtp:Host is needed.");
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_the_reason_of_a_failed_test_mail_short_and_in_the_log()
    {
        _delivered = false;

        var result = await Notifier.SendTestAsync(Token);

        result.Delivered.Should().BeFalse();
        _store.Log.Single().Error.Should().Be("The mail server could not be reached.");
    }

    [Fact]
    public async Task should_forget_the_attempts_older_than_thirty_days_once_a_day()
    {
        await _store.RecordAsync(new NotificationLogEntry(0, "test", null, null, "info", 0, Start.AddDays(-31), true, null), Token);
        await _store.RecordAsync(new NotificationLogEntry(0, "test", null, null, "info", 0, Start.AddDays(-2), true, null), Token);

        _ = await Notifier.RunOnceAsync(Token);

        _store.Log.Should().ContainSingle().Which.SentAt.Should().Be(Start.AddDays(-2));
    }
}
