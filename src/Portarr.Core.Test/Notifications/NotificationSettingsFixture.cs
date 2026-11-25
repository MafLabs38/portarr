using System.Text.Json;
using MafLabs.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Portarr.Core.Configuration;
using Portarr.Core.Notifications;

namespace Portarr.Core.Test.Notifications;

public sealed class NotificationSettingsFixture
{
    private readonly Dictionary<string, string> _config = [];
    private readonly IConfigRepository _repository = Substitute.For<IConfigRepository>();
    private readonly NotificationPolicyOptions _policy = new();
    private readonly PortarrNotificationOptions _configured = new()
    {
        Smtp = new SmtpOptions { Host = "smtp.homelab.example", UserName = "portarr", PasswordFile = "/run/secrets/does-not-exist" },
    };

    public NotificationSettingsFixture()
    {
        _repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(_config.GetValueOrDefault(call.Arg<string>())));
        _repository.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _config[call.ArgAt<string>(0)] = call.ArgAt<string>(1);
            return Task.CompletedTask;
        });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private NotificationSettings Subject => new(Options.Create(_configured), Options.Create(_policy), TestCategories.Declared(), _repository, NotificationProviders.BuiltIn, NullLogger<NotificationSettings>.Instance);

    private static NotificationCategoryChange Category(string id, bool enabled = true, NotificationLevel level = NotificationLevel.Warning, int hours = 12, int reminders = 2, int perDay = 2) =>
        new(id, enabled, level, hours, reminders, perDay);

    private static NotificationSettingsChange On(string? recipient = "operator@homelab.example", params NotificationCategoryChange[] categories) =>
        new(true, recipient, "portarr@homelab.example", 5, categories);

    [Fact]
    public async Task should_use_the_configuration_and_the_defaults_of_the_categories_when_nothing_was_saved()
    {
        var state = await Subject.GetAsync(Token);

        state.Options.Enabled.Should().BeFalse("off by default");
        state.Options.Smtp.Host.Should().Be("smtp.homelab.example");
        state.Policy.MaxPerDay.Should().Be(10);
        state.Categories.Keys.Should().BeEquivalentTo("apps", "infrastructure", "data", "maintenance");
        state.Categories["maintenance"].Enabled.Should().BeFalse("maintenance is off by default");
        state.Categories["apps"].Should().BeEquivalentTo(new CategoryPolicy { Enabled = true, MinimumLevel = NotificationLevel.Danger, ReminderHours = 72, MaxReminders = 1, MaxPerDay = 2 });
        state.Categories["data"].MinimumLevel.Should().Be(NotificationLevel.Warning);
    }

    [Fact]
    public async Task should_apply_what_was_saved_at_once_and_after_a_restart_and_keep_the_connection_of_the_deployment()
    {
        var subject = Subject;

        var outcome = await subject.UpdateAsync(On(categories: [Category("data", level: NotificationLevel.Info, hours: 6), Category("maintenance", enabled: true)]), Token);

        outcome.Saved.Should().BeTrue();
        var live = ((IOptions<NotificationOptions>)subject).Value.Should().BeOfType<PortarrNotificationOptions>().Subject;
        live.Enabled.Should().BeTrue();
        live.Recipient.Should().Be("operator@homelab.example");

        var restarted = await Subject.GetAsync(Token);
        restarted.Options.Recipient.Should().Be("operator@homelab.example");
        restarted.Policy.MaxPerDay.Should().Be(5);
        restarted.Policy.ConfirmMinutes.Should().Be(_policy.ConfirmMinutes);
        restarted.Categories["data"].Should().BeEquivalentTo(new CategoryPolicy { Enabled = true, MinimumLevel = NotificationLevel.Info, ReminderHours = 6, MaxReminders = 2, MaxPerDay = 2 });
        restarted.Categories["maintenance"].Enabled.Should().BeTrue();
        restarted.Categories["apps"].Enabled.Should().BeTrue("a category that was not in the change keeps its policy");
        restarted.Options.Smtp.Host.Should().Be("smtp.homelab.example");
        restarted.Options.Smtp.PasswordFile.Should().Be("/run/secrets/does-not-exist");
    }

    [Fact]
    public async Task should_store_the_non_secret_settings_with_a_map_of_categories_and_no_connection_setting()
    {
        _ = await Subject.UpdateAsync(On(categories: [Category("data")]), Token);

        var stored = _config["Notifications"];
        stored.Should().NotContain("smtp.homelab.example").And.NotContain("passwordFile").And.NotContain("\"host\"").And.NotContain("does-not-exist");
        using var document = JsonDocument.Parse(stored);
        document.RootElement.GetProperty("recipient").GetString().Should().Be("operator@homelab.example");
        document.RootElement.GetProperty("maxPerDay").GetInt32().Should().Be(5);
        var data = document.RootElement.GetProperty("categories").GetProperty("data");
        data.GetProperty("minimumLevel").GetString().Should().Be("warning");
        data.GetProperty("reminderHours").GetInt32().Should().Be(12);
    }

    [Fact]
    public async Task should_refuse_to_turn_on_when_the_recipient_is_missing_and_store_nothing()
    {
        var subject = Subject;

        var outcome = await subject.UpdateAsync(On(recipient: null), Token);

        outcome.Saved.Should().BeFalse();
        outcome.Errors["settings"].Single().Should().Contain("Notifications:Recipient");
        _config.Should().BeEmpty();
        (await subject.GetAsync(Token)).Options.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task should_refuse_to_turn_on_when_the_connection_is_not_deployed()
    {
        _configured.Smtp.Host = null;

        var outcome = await Subject.UpdateAsync(On(), Token);

        outcome.Errors["settings"].Single().Should().Contain("Notifications:Smtp:Host");
    }

    [Fact]
    public async Task should_accept_a_change_that_leaves_notifications_off_without_a_connection()
    {
        _configured.Smtp.Host = null;

        var outcome = await Subject.UpdateAsync(new NotificationSettingsChange(false, null, null, 10, []), Token);

        outcome.Saved.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 2, 2, "reminderHours")]
    [InlineData(721, 2, 2, "reminderHours")]
    [InlineData(12, 31, 2, "maxReminders")]
    [InlineData(12, -1, 2, "maxReminders")]
    [InlineData(12, 2, 0, "maxPerDay")]
    [InlineData(12, 2, 1001, "maxPerDay")]
    public async Task should_refuse_a_value_of_a_category_out_of_range_and_name_the_field(int hours, int reminders, int perDay, string field)
    {
        var outcome = await Subject.UpdateAsync(On(categories: [Category("data", hours: hours, reminders: reminders, perDay: perDay)]), Token);

        outcome.Saved.Should().BeFalse();
        outcome.Errors.Should().ContainKey($"categories.data.{field}");
        outcome.Errors.Keys.Should().OnlyContain(k => k == $"categories.data.{field}");
        _config.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_the_level_ok_and_a_level_that_does_not_exist()
    {
        var ok = await Subject.UpdateAsync(On(categories: [Category("apps", level: NotificationLevel.Ok)]), Token);
        var unknown = await Subject.UpdateAsync(On(categories: [Category("apps", level: (NotificationLevel)42)]), Token);

        ok.Errors.Should().ContainKey("categories.apps.minimumLevel");
        unknown.Errors.Should().ContainKey("categories.apps.minimumLevel");
    }

    [Fact]
    public async Task should_refuse_an_overall_daily_limit_out_of_range()
    {
        var outcome = await Subject.UpdateAsync(On() with { MaxPerDay = 0 }, Token);

        outcome.Errors["maxPerDay"].Single().Should().Contain("MaxPerDay");
        _config.Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_category_that_does_not_exist_or_is_given_twice_and_report_every_error_at_once()
    {
        var outcome = await Subject.UpdateAsync(On(categories: [Category("billing"), Category("apps"), Category("apps"), Category("data", hours: 0)]) with { MaxPerDay = 0 }, Token);

        outcome.Saved.Should().BeFalse();
        outcome.Errors["categories"].Should().HaveCount(2).And.Contain(m => m.Contains("'billing'")).And.Contain(m => m.Contains("twice"));
        outcome.Errors.Should().ContainKey("categories.data.reminderHours").And.ContainKey("maxPerDay");
        _config.Should().BeEmpty();
    }

    [Fact]
    public async Task should_use_the_configuration_when_the_stored_value_cannot_be_read()
    {
        _config["Notifications"] = "{ this is not json";

        var state = await Subject.GetAsync(Token);

        state.Options.Enabled.Should().BeFalse();
        state.Options.Smtp.Host.Should().Be("smtp.homelab.example");
        state.Categories["data"].MinimumLevel.Should().Be(NotificationLevel.Warning);
    }

    [Fact]
    public async Task should_fall_back_to_the_default_of_a_category_whose_stored_policy_is_corrupted_and_keep_the_others()
    {
        _config["Notifications"] = """
            {"enabled":true,"recipient":"operator@homelab.example","from":"portarr@homelab.example","maxPerDay":7,
             "categories":{"data":{"enabled":true,"minimumLevel":"info","reminderHours":0,"maxReminders":3,"maxPerDay":3},
                           "apps":{"enabled":false,"minimumLevel":"warning","reminderHours":6,"maxReminders":1,"maxPerDay":1},
                           "removed":{"enabled":true,"minimumLevel":"info","reminderHours":6,"maxReminders":1,"maxPerDay":1}}}
            """;

        var state = await Subject.GetAsync(Token);

        state.Categories["data"].Should().BeEquivalentTo(new CategoryPolicy { Enabled = true, MinimumLevel = NotificationLevel.Warning, ReminderHours = 72, MaxReminders = 1, MaxPerDay = 2 }, "the default of data");
        state.Categories["apps"].Should().BeEquivalentTo(new CategoryPolicy { Enabled = false, MinimumLevel = NotificationLevel.Warning, ReminderHours = 6, MaxReminders = 1, MaxPerDay = 1 });
        state.Categories.Keys.Should().NotContain("removed");
        state.Options.Enabled.Should().BeTrue();
        state.Policy.MaxPerDay.Should().Be(7);
    }

    [Fact]
    public async Task should_fall_back_to_the_configured_daily_limit_when_the_stored_one_is_corrupted()
    {
        _config["Notifications"] = """{"enabled":false,"maxPerDay":0,"categories":{}}""";

        (await Subject.GetAsync(Token)).Policy.MaxPerDay.Should().Be(10);
    }

    [Fact]
    public async Task should_fall_back_to_the_default_when_the_stored_level_is_a_number_that_means_nothing()
    {
        _config["Notifications"] = """{"enabled":false,"categories":{"apps":{"enabled":true,"minimumLevel":9,"reminderHours":6,"maxReminders":1,"maxPerDay":1}}}""";

        (await Subject.GetAsync(Token)).Categories["apps"].MinimumLevel.Should().Be(NotificationLevel.Danger);
    }

    [Fact]
    public void should_say_when_the_password_file_cannot_be_read_and_nothing_when_there_is_none()
    {
        var subject = Subject;

        subject.CheckSecrets(_configured).Should().Be("The file of the mail password cannot be read.");

        _configured.Smtp.UserName = null;
        _configured.Smtp.PasswordFile = null;
        subject.CheckSecrets(_configured).Should().BeNull();
    }

    [Fact]
    public void should_refuse_an_interval_between_looks_that_cannot_work_when_turned_on()
    {
        _configured.Enabled = true;
        _configured.Recipient = "operator@homelab.example";
        _configured.From = "portarr@homelab.example";
        _configured.PassSeconds = 1;

        Subject.Check(_configured).Should().Contain("PassSeconds");
    }
}
