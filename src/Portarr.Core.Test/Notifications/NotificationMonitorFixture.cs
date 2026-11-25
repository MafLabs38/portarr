using MafLabs.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Portarr.Core.Attention;
using Portarr.Core.Notifications;

namespace Portarr.Core.Test.Notifications;

public sealed class NotificationMonitorFixture
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);

    [Fact]
    public async Task should_keep_looking_when_a_pass_fails_and_send_when_a_mail_is_due()
    {
        var calls = 0;
        var attention = Substitute.For<IAttentionService>();
        attention.GetFindings().Returns(_ =>
        {
            calls++;
            return calls == 1 ? throw new InvalidOperationException("a tool failed") : [Findings.AppDown("wiki")];
        });
        var sender = Substitute.For<INotificationSender>();
        sender.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns(NotificationResult.Done);
        var settings = new FakeNotificationSettings(new PortarrNotificationOptions { Enabled = true, Recipient = "operator@homelab.example", From = "portarr@homelab.example", PassSeconds = 60 })
        {
            Policy = new NotificationPolicyOptions { ConfirmMinutes = 0 },
        };
        var notifier = new AttentionNotifier(attention, settings, TestCategories.Declared(), sender, new FakeNotificationStore(), _clock, NullLogger<AttentionNotifier>.Instance);
        var monitor = new NotificationMonitor(notifier, settings, _clock, NullLogger<NotificationMonitor>.Instance);
        using var cancellation = new CancellationTokenSource();

        await monitor.StartAsync(cancellation.Token);
        await AdvanceUntilAsync(() => sender.ReceivedCalls().Any());
        await cancellation.CancelAsync();
        await monitor.StopAsync(TestContext.Current.CancellationToken);

        calls.Should().BeGreaterThanOrEqualTo(2, "the loop survived the first pass throwing");
        await sender.Received(1).SendAsync(Arg.Is<Notification>(n => n.Recipient == "operator@homelab.example"), Arg.Any<CancellationToken>());
    }

    private async Task AdvanceUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            _clock.Advance(TimeSpan.FromSeconds(60));
        }
    }
}
