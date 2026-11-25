using MafLabs.Notifications;
using Portarr.Core.Attention;
using Portarr.Core.Maintenance;
using Portarr.Core.Notifications;

namespace Portarr.Core.Test.Notifications;

public sealed class NotificationTemplatesFixture
{
    private static NotificationCategory Category(string id) => TestCategories.Declared().Get(id);

    private static RenderedMail Render(string category, AttentionFinding[] findings, NotificationLevel level = NotificationLevel.Danger, int reminder = 0, int maxReminders = 3, bool willRemind = true, int reminderHours = 24) =>
        NotificationTemplates.Render(Category(category), findings, level, reminder, maxReminders, willRemind, reminderHours);

    [Theory]
    [InlineData("apps", "Applications need attention")]
    [InlineData("infrastructure", "Infrastructure needs attention")]
    [InlineData("data", "Backups need attention")]
    [InlineData("maintenance", "Maintenance needs attention")]
    public void should_write_a_mail_with_a_title_of_its_own_for_each_category(string category, string title)
    {
        var mail = Render(category, [new AttentionFinding(category, AttentionLevel.Danger, "x-kind", 1, ["thing"])]);

        mail.Subject.Should().Be($"Portarr: {title} (new)");
        mail.Html.Should().Contain($"<h1").And.Contain($">{title}</h1>").And.Contain("<title>" + title + "</title>");
        mail.Text.Should().Contain(title).And.Contain("Portarr");
    }

    [Theory]
    [InlineData("apps", "Apps")]
    [InlineData("infrastructure", "Infrastructure")]
    [InlineData("data", "Data")]
    [InlineData("maintenance", "Maintenance")]
    public void should_tell_the_operator_which_page_of_portarr_to_open(string category, string page)
    {
        var mail = Render(category, [new AttentionFinding(category, AttentionLevel.Danger, "x-kind", 1, ["thing"])]);

        mail.Text.Should().Contain($"Open the {page} page of Portarr to see the details.");
        mail.Html.Should().Contain($"Open the {page} page of Portarr to see the details.");
    }

    [Fact]
    public void should_list_each_name_with_its_level_and_the_sentence_of_its_kind_in_the_html_and_the_text()
    {
        var mail = Render("data", [Findings.BackupFailed("media (critical)"), Findings.BackupLate("photos (important)")]);

        mail.Text.Should().Contain("- Critical - Backups are failing: media (critical)")
            .And.Contain("- Warning - Backups are late: photos (important)")
            .And.Contain("2 data asset(s) need attention");
        mail.Html.Should().Contain("<li").And.Contain("Backups are failing: media (critical)").And.Contain("Backups are late: photos (important)");
    }

    [Fact]
    public void should_make_one_mail_of_several_findings_of_the_category()
    {
        var mail = Render("data", [Findings.BackupFailed("media"), Findings.BackupMissing("notes"), Findings.BackupLate("photos")]);

        mail.Text.Should().Contain("Backups are failing: media").And.Contain("Backups are missing: notes").And.Contain("Backups are late: photos").And.Contain("3 data asset(s)");
    }

    [Fact]
    public void should_color_the_mail_with_the_accent_of_its_level_and_name_the_level()
    {
        var danger = Render("apps", [Findings.AppDown("wiki")], NotificationLevel.Danger);
        var warning = Render("data", [Findings.BackupLate("photos")], NotificationLevel.Warning);

        danger.Html.Should().Contain(MailLayout.AccentOf(NotificationLevel.Danger)).And.NotContain(MailLayout.AccentOf(NotificationLevel.Warning));
        danger.Text.Should().StartWith("[Critical] Portarr - Apps");
        warning.Html.Should().Contain(MailLayout.AccentOf(NotificationLevel.Warning)).And.NotContain(MailLayout.AccentOf(NotificationLevel.Danger));
        warning.Text.Should().StartWith("[Warning] Portarr - Data");
        danger.Context!.Level.Should().Be(NotificationLevel.Danger);
    }

    [Fact]
    public void should_never_write_a_hostile_name_raw_in_the_html_nor_let_it_inject_a_header_in_the_text()
    {
        const string hostile = "<script>alert(1)</script>\r\nBcc: attacker@homelab.example";

        var mail = Render("apps", [Findings.AppDown(hostile)]);

        mail.Html.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
        mail.Text.Should().NotContain("\r").And.NotContain("\nBcc:");
        mail.ToNotification("operator@homelab.example").Subject.Should().NotContain("\n");
    }

    [Fact]
    public void should_say_which_reminder_it_is_and_that_it_is_the_last_when_no_other_will_follow()
    {
        var mail = Render("data", [Findings.BackupFailed("media")], reminder: 3, maxReminders: 3, willRemind: false);

        mail.Subject.Should().Contain("reminder 3 of 3");
        mail.Text.Should().Contain("will not write again");
    }

    [Fact]
    public void should_say_when_the_next_reminder_comes()
    {
        var mail = Render("data", [Findings.BackupFailed("media")], reminderHours: 12);

        mail.Text.Should().Contain("reminds you again in 12 hour(s)");
        mail.Html.Should().Contain("reminds you again in 12 hour(s)");
    }

    [Fact]
    public void should_fall_back_to_the_kind_when_the_sentence_is_not_known()
    {
        var mail = Render("maintenance", [new AttentionFinding("maintenance", AttentionLevel.Danger, "image-vulnerable", 1, ["web"])]);

        mail.Text.Should().Contain("Image vulnerable: web");
        NotificationTemplates.SentenceOf("image-vulnerable").Should().Be("image vulnerable");
    }

    [Theory]
    [InlineData("backup-failed", "backups are failing")]
    [InlineData("data-missing", "backups are missing")]
    [InlineData("backup-late", "backups are late")]
    [InlineData("app-down", "apps are down")]
    [InlineData("environment-unreachable", "environments cannot be reached")]
    [InlineData("machine-not-ready", "machines are not ready")]
    [InlineData("severe-alerts", "repositories have severe security alerts")]
    [InlineData("platform-out-of-support", "platforms are out of support")]
    [InlineData("ci-failing", "continuous integration is failing")]
    public void should_have_a_sentence_for_the_kinds_of_finding_that_are_known(string kind, string sentence)
    {
        NotificationTemplates.SentenceOf(kind).Should().Be(sentence);
    }

    [Fact]
    public void should_write_only_the_sentence_when_the_names_are_what_a_remote_tool_said()
    {
        var mail = Render("maintenance", [new AttentionFinding("maintenance", AttentionLevel.Warning, "code-source-error", 1, ["the remote said: token ghp_secret rejected"])], NotificationLevel.Warning);

        mail.Text.Should().Contain("The code source cannot be read (1)").And.NotContain("ghp_secret");
        mail.Html.Should().NotContain("ghp_secret");
    }

    [Fact]
    public void should_count_what_does_not_fit_when_there_are_many_names()
    {
        var names = Enumerable.Range(1, 70).Select(i => $"asset-number-{i} (critical)").ToArray();

        var mail = Render("data", [Findings.BackupFailed(names)]);

        mail.Text.Should().Contain("asset-number-1 (critical)").And.Contain("and 20 more").And.NotContain("asset-number-70");
        mail.Html.Should().Contain("and 20 more");
    }

    [Fact]
    public void should_write_a_test_mail_in_html_and_text()
    {
        var mail = NotificationTemplates.RenderTest();

        mail.Subject.Should().Be("Portarr: a test mail");
        mail.Text.Should().Contain("check that the notifications work");
        mail.Html.Should().Contain("<!DOCTYPE html>").And.Contain("Notifications work").And.Contain("check that the notifications work");
        mail.ToNotification("operator@homelab.example").Content.Should().NotBeNull();
    }

    [Fact]
    public void should_fall_back_to_a_generic_template_for_a_category_it_has_no_text_for()
    {
        var other = new NotificationCategory("billing", "Billing", "Bills.", new CategoryPolicy());

        var mail = NotificationTemplates.Render(other, [new AttentionFinding("billing", AttentionLevel.Danger, "x", 1, ["a"])], NotificationLevel.Danger, 0, 3, true, 24);

        mail.Subject.Should().Be("Portarr: Billing needs attention (new)");
    }
}
