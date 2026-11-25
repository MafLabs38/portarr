using System.Net;
using System.Net.Http.Json;
using MafLabs.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portarr.Api.V1.Notifications;

namespace Portarr.Integration.Test.Api;

[Trait("Category", "Integration")]
public sealed class NotificationsFixture
{
    private const string PasswordInTheFile = "app-password-that-must-never-leave";

    private readonly TestDatabase _database;

    public NotificationsFixture(TestDatabase database)
    {
        _database = database;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static NotificationCategoryUpdate Category(string id, bool enabled = true, string level = "warning", int hours = 12, int reminders = 2, int perDay = 2) =>
        new(id, enabled, level, hours, reminders, perDay);

    private static NotificationSettingsUpdate On(params NotificationCategoryUpdate[] categories) =>
        new(true, "operator@homelab.example", "portarr@homelab.example", 5, categories);

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, NotificationSettingsUpdate update) =>
        client.PutAsJsonAsync("/api/v1/notifications/settings", update, Token);

    private static async Task<IDictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Token))!.Errors;

    private async Task<(PortarrFactory Factory, SentMails Mails, string PasswordFile)> StartAsync(bool deployed = true, bool delivers = true)
    {
        var mails = new SentMails(delivers);
        var passwordFile = Path.Combine((await _database.CreateOptionsAsync()).DataFolder, "smtp-password");
        await File.WriteAllTextAsync(passwordFile, PasswordInTheFile + "\n", Token);
        var settings = deployed
            ? new Dictionary<string, string?>
            {
                ["Notifications:Smtp:Host"] = "smtp.homelab.example",
                ["Notifications:Smtp:UserName"] = "portarr",
                ["Notifications:Smtp:PasswordFile"] = passwordFile,
            }
            : new Dictionary<string, string?>();
        var factory = new PortarrFactory(
            await _database.CreateOptionsAsync(),
            settings,
            services =>
            {
                services.RemoveAll<INotificationSender>();
                services.AddSingleton<INotificationSender>(mails);
            });
        return (factory, mails, passwordFile);
    }

    [Fact]
    public async Task should_be_off_by_default_and_say_how_the_mail_goes_out_without_the_password_or_its_path()
    {
        var (factory, _, passwordFile) = await StartAsync();
        await using var _1 = factory;
        using var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync("/api/v1/notifications/settings", Token);
        var text = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        text.Should().NotContain(PasswordInTheFile).And.NotContain(passwordFile);
        var settings = (await response.Content.ReadFromJsonAsync<NotificationSettingsResource>(Token))!;
        settings.Enabled.Should().BeFalse();
        settings.MaxPerDay.Should().Be(10);
        settings.Categories.Select(c => c.Id).Should().Equal("apps", "infrastructure", "data", "maintenance");
        settings.Categories.Single(c => c.Id == "data").Should().BeEquivalentTo(new NotificationCategoryResource(
            "data", "Data", settings.Categories[2].Description, true, "warning", 72, 1, 2, new NotificationCategoryPolicyResource(true, "warning", 72, 1, 2)));
        settings.Categories.Single(c => c.Id == "maintenance").Should().Match<NotificationCategoryResource>(c => !c.Enabled && c.MinimumLevel == "danger" && !c.Defaults.Enabled);
        settings.Categories.Where(c => c.Id != "maintenance").Should().OnlyContain(c => c.Enabled && c.Title.Length > 0 && c.Description.Length > 0);
        settings.Connection.Should().BeEquivalentTo(new NotificationConnectionResource("Smtp", "smtp.homelab.example", 587, "portarr", true, true));
        settings.Problem.Should().BeNull();
        settings.SecretProblem.Should().BeNull();
        settings.LastAttempt.Should().BeNull();
    }

    [Fact]
    public async Task should_say_when_the_password_file_cannot_be_read()
    {
        var (factory, _, passwordFile) = await StartAsync();
        await using var _1 = factory;
        File.Delete(passwordFile);
        using var client = await factory.CreateSignedInClientAsync();

        var settings = await client.GetFromJsonAsync<NotificationSettingsResource>("/api/v1/notifications/settings", Token);

        settings!.SecretProblem.Should().Be("The file of the mail password cannot be read.");
    }

    [Fact]
    public async Task should_keep_the_settings_across_restarts_and_never_return_the_password()
    {
        var options = await _database.CreateOptionsAsync();
        var mails = new SentMails(true);
        var deployed = new Dictionary<string, string?> { ["Notifications:Smtp:Host"] = "smtp.homelab.example" };

        await using (var first = new PortarrFactory(options, deployed, s => s.Replace(ServiceDescriptor.Singleton<INotificationSender>(mails))))
        {
            using var client = await first.CreateSignedInClientAsync();
            var response = await PutAsync(client, On(Category("data", level: "info", hours: 6), Category("maintenance")));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync(Token)).Should().NotContain(PasswordInTheFile);
        }

        await using var second = new PortarrFactory(options, deployed, s => s.Replace(ServiceDescriptor.Singleton<INotificationSender>(mails)));
        using var restarted = await second.CreateSignedInClientAsync();
        var settings = await restarted.GetFromJsonAsync<NotificationSettingsResource>("/api/v1/notifications/settings", Token);

        settings.Should().BeEquivalentTo(new { Enabled = true, Recipient = "operator@homelab.example", From = "portarr@homelab.example", MaxPerDay = 5 });
        settings!.Categories.Single(c => c.Id == "data").Should().BeEquivalentTo(new { Enabled = true, MinimumLevel = "info", ReminderHours = 6, MaxReminders = 2, MaxPerDay = 2 });
        settings.Categories.Single(c => c.Id == "maintenance").Enabled.Should().BeTrue();
        settings.Categories.Single(c => c.Id == "maintenance").Defaults.Enabled.Should().BeFalse();
        settings.Categories.Single(c => c.Id == "apps").Should().BeEquivalentTo(new { Enabled = true, MinimumLevel = "danger", ReminderHours = 72, MaxReminders = 1, MaxPerDay = 2 });
    }

    [Fact]
    public async Task should_refuse_to_turn_on_when_the_connection_is_not_deployed()
    {
        var (factory, _, _) = await StartAsync(deployed: false);
        await using var _1 = factory;
        using var client = await factory.CreateSignedInClientAsync();

        var response = await PutAsync(client, On());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsAsync(response)).Should().ContainKey("settings").WhoseValue.Single().Should().Contain("Notifications:Smtp:Host");
        (await client.GetFromJsonAsync<NotificationSettingsResource>("/api/v1/notifications/settings", Token))!.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("data", "info", 0, 2, 2, "categories.data.reminderHours")]
    [InlineData("data", "info", 721, 2, 2, "categories.data.reminderHours")]
    [InlineData("apps", "danger", 12, 31, 2, "categories.apps.maxReminders")]
    [InlineData("infrastructure", "danger", 12, 2, 0, "categories.infrastructure.maxPerDay")]
    [InlineData("maintenance", "ok", 12, 2, 2, "categories.maintenance.minimumLevel")]
    [InlineData("maintenance", "loud", 12, 2, 2, "categories.maintenance.minimumLevel")]
    public async Task should_refuse_a_value_of_a_category_that_cannot_work_and_name_the_field(string id, string level, int hours, int reminders, int perDay, string key)
    {
        var (factory, _, _) = await StartAsync();
        await using var _1 = factory;
        using var client = await factory.CreateSignedInClientAsync();

        var response = await PutAsync(client, On(Category(id, level: level, hours: hours, reminders: reminders, perDay: perDay)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsAsync(response)).Keys.Should().Contain(key);
        (await client.GetFromJsonAsync<NotificationSettingsResource>("/api/v1/notifications/settings", Token))!.Enabled.Should().BeFalse("nothing was stored");
    }

    [Theory]
    [InlineData("not-an-address", "portarr@homelab.example", 5, "recipient")]
    [InlineData("operator@homelab.example", "a\nb@homelab.example", 5, "from")]
    [InlineData("operator@homelab.example", "portarr@homelab.example", 0, "maxPerDay")]
    [InlineData("operator@homelab.example", "portarr@homelab.example", 1001, "maxPerDay")]
    public async Task should_refuse_a_global_value_that_cannot_work(string recipient, string from, int perDay, string key)
    {
        var (factory, _, _) = await StartAsync();
        await using var _1 = factory;
        using var client = await factory.CreateSignedInClientAsync();

        var response = await PutAsync(client, new NotificationSettingsUpdate(true, recipient, from, perDay, [Category("apps")]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsAsync(response)).Keys.Should().Contain(key);
    }

    [Fact]
    public async Task should_refuse_an_unknown_category_and_a_missing_field()
    {
        var (factory, _, _) = await StartAsync();
        await using var _1 = factory;
        using var client = await factory.CreateSignedInClientAsync();

        var unknown = await PutAsync(client, On(Category("billing")));
        var missing = await PutAsync(client, new NotificationSettingsUpdate(true, "operator@homelab.example", "portarr@homelab.example", 5, [new NotificationCategoryUpdate("apps", true, "danger", null, 2, 2)]));
        var noCategories = await PutAsync(client, new NotificationSettingsUpdate(true, "operator@homelab.example", "portarr@homelab.example", null, null));

        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsAsync(unknown)).Should().ContainKey("categories").WhoseValue.Single().Should().Contain("'billing'");
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsAsync(missing)).Keys.Should().Contain("categories.apps.reminderHours");
        noCategories.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsAsync(noCategories)).Keys.Should().Contain("maxPerDay").And.Contain("categories");
    }

    [Fact]
    public async Task should_send_a_test_mail_to_the_recipient_and_log_it()
    {
        var (factory, mails, _) = await StartAsync();
        await using var _1 = factory;
        using var client = await factory.CreateSignedInClientAsync();
        (await PutAsync(client, On())).EnsureSuccessStatusCode();

        var result = await (await client.PostAsync("/api/v1/notifications/test", null, Token)).Content.ReadFromJsonAsync<NotificationTestResource>(Token);

        result.Should().BeEquivalentTo(new NotificationTestResource(true, null));
        mails.Sent.Should().ContainSingle().Which.Recipient.Should().Be("operator@homelab.example");
        var log = await client.GetFromJsonAsync<NotificationLogResource>("/api/v1/notifications/log", Token);
        log!.Entries.Should().ContainSingle().Which.Should().Match<NotificationLogEntryResource>(e => e.Kind == "test" && e.Category == null && e.Level == "info" && e.Delivered && e.Error == null);
        (await client.GetFromJsonAsync<NotificationSettingsResource>("/api/v1/notifications/settings", Token))!.LastAttempt!.Kind.Should().Be("test");
        mails.Sent.Single().Content.Should().NotBeNull();
    }

    [Fact]
    public async Task should_say_why_a_test_mail_did_not_leave_and_keep_it_in_the_log()
    {
        var (factory, _, _) = await StartAsync(delivers: false);
        await using var _1 = factory;
        using var client = await factory.CreateSignedInClientAsync();
        (await PutAsync(client, On())).EnsureSuccessStatusCode();

        var result = await (await client.PostAsync("/api/v1/notifications/test", null, Token)).Content.ReadFromJsonAsync<NotificationTestResource>(Token);

        result.Should().BeEquivalentTo(new NotificationTestResource(false, "The mail server could not be reached."));
        var log = await client.GetFromJsonAsync<NotificationLogResource>("/api/v1/notifications/log", Token);
        log!.Entries.Single().Should().Match<NotificationLogEntryResource>(e => !e.Delivered && e.Error == "The mail server could not be reached.");
    }

    [Fact]
    public async Task should_not_send_a_test_mail_while_notifications_are_off()
    {
        var (factory, mails, _) = await StartAsync();
        await using var _1 = factory;
        using var client = await factory.CreateSignedInClientAsync();

        var result = await (await client.PostAsync("/api/v1/notifications/test", null, Token)).Content.ReadFromJsonAsync<NotificationTestResource>(Token);

        result.Should().BeEquivalentTo(new NotificationTestResource(false, "Notifications are off."));
        mails.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Observer")]
    public async Task should_keep_the_notifications_to_whoever_may_change_the_settings(string role)
    {
        var (factory, _, _) = await StartAsync();
        await using var _1 = factory;
        using var client = await factory.CreateSignedInClientAsync(role);

        (await client.GetAsync("/api/v1/notifications/settings", Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PutAsync(client, On())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync("/api/v1/notifications/test", null, Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/notifications/log", Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed class SentMails : INotificationSender
    {
        private readonly bool _delivers;

        public SentMails(bool delivers)
        {
            _delivers = delivers;
        }

        public List<Notification> Sent { get; } = [];

        public Task<NotificationResult> SendAsync(Notification notification, CancellationToken cancellationToken)
        {
            Sent.Add(notification);
            return Task.FromResult(_delivers ? NotificationResult.Done : NotificationResult.Failed("The mail server could not be reached."));
        }
    }
}
