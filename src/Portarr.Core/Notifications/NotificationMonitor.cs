using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Portarr.Core.Notifications;

/// <summary>
/// Looks at what needs attention now and then and lets <see cref="AttentionNotifier"/> write when a mail is due. It does nothing while notifications are
/// off (the default), and a pass that fails never ends the service: the next one tries again.
/// </summary>
public sealed partial class NotificationMonitor : BackgroundService
{
    private readonly AttentionNotifier _notifier;
    private readonly INotificationSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationMonitor> _logger;

    public NotificationMonitor(AttentionNotifier notifier, INotificationSettings settings, TimeProvider timeProvider, ILogger<NotificationMonitor> logger)
    {
        _notifier = notifier;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan period;
        try
        {
            period = TimeSpan.FromSeconds(Math.Clamp((await _settings.GetAsync(stoppingToken)).Options.PassSeconds, PortarrNotificationOptions.MinPassSeconds, PortarrNotificationOptions.MaxPassSeconds));
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // The stored settings cannot be read: the standard period, and each pass reads them again.
            LogSettingsFailed(exception);
            period = TimeSpan.FromSeconds(new PortarrNotificationOptions().PassSeconds);
        }

        using var timer = new PeriodicTimer(period, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var sent = await _notifier.RunOnceAsync(stoppingToken);
                if (sent > 0)
                {
                    LogSent(sent);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // A failed pass must never end the service (and with it the host).
                LogPassFailed(exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} notification mail(s) were sent")]
    private partial void LogSent(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass of the notifications failed")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification settings could not be read at start")]
    private partial void LogSettingsFailed(Exception exception);
}
