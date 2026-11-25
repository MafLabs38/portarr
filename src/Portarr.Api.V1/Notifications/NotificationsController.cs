using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using MafLabs.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Core.Notifications;

namespace Portarr.Api.V1.Notifications;

/// <summary>
/// The mail Portarr sends to the operator when something needs attention (D-129, ADR 0022). Everything here is for whoever may change the settings:
/// the recipient is theirs to set, and the password of the mail account is never returned (only whether a file holds one).
/// </summary>
[SuppressMessage("Major Code Smell", "S6960:This controller has multiple responsibilities and could be split into 2 smaller controllers.", Justification = "One resource of the settings page: the settings, the test mail and the log of the mails. Splitting them would put three classes on one route prefix.")]
[ApiController]
[Authorize(Policy = Permissions.SettingsManage)]
[Route("api/v1/notifications")]
[Produces("application/json")]
public sealed class NotificationsController : ControllerBase
{
    private const int LogSize = 50;

    private readonly INotificationSettings _settings;
    private readonly NotificationCategories _categories;
    private readonly INotificationStore _store;
    private readonly AttentionNotifier _notifier;
    private readonly IValidator<NotificationSettingsUpdate> _validator;

    public NotificationsController(INotificationSettings settings, NotificationCategories categories, INotificationStore store, AttentionNotifier notifier, IValidator<NotificationSettingsUpdate> validator)
    {
        _settings = settings;
        _categories = categories;
        _store = store;
        _notifier = notifier;
        _validator = validator;
    }

    /// <summary>Whether Portarr writes, to whom, what each category does, and how the mail goes out (without the password).</summary>
    /// <response code="200">The settings in force and the latest attempt.</response>
    [HttpGet("settings")]
    [ProducesResponseType<NotificationSettingsResource>(StatusCodes.Status200OK)]
    public async Task<NotificationSettingsResource> GetSettingsAsync(CancellationToken cancellationToken)
    {
        return await ReadAsync(await _settings.GetAsync(cancellationToken), cancellationToken);
    }

    /// <summary>Changes whether Portarr writes, to whom, and what each category does; applied at once and kept across restarts.</summary>
    /// <remarks>
    /// The connection to the mail server (host, port, user, the file that holds the password) is not editable here: it is a setting of the deployment.
    /// Turning notifications on is refused (400) while the recipient, the sender or the connection are missing or wrong. A refused change says what is wrong
    /// by field (<c>maxPerDay</c>, <c>categories.data.reminderHours</c>, <c>settings</c>…) and stores nothing.
    /// </remarks>
    /// <response code="200">The settings now in force.</response>
    /// <response code="400">A value is out of range, or the settings cannot work when turned on; nothing was stored.</response>
    [HttpPut("settings")]
    [ProducesResponseType<NotificationSettingsResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationSettingsResource>> UpdateSettingsAsync(NotificationSettingsUpdate update, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(update, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        var outcome = await _settings.UpdateAsync(update.ToChange(), cancellationToken);
        if (!outcome.Saved)
        {
            return ValidationProblem(new ValidationProblemDetails(outcome.Errors.ToDictionary(e => e.Key, e => e.Value)));
        }

        return await ReadAsync(outcome.State, cancellationToken);
    }

    /// <summary>Sends a test mail to the recipient, to see that the settings work. It counts in the daily limit of mails.</summary>
    /// <response code="200">Whether it was sent and, if not, why (also when notifications are off).</response>
    [HttpPost("test")]
    [ProducesResponseType<NotificationTestResource>(StatusCodes.Status200OK)]
    public async Task<NotificationTestResource> TestAsync(CancellationToken cancellationToken)
    {
        var result = await _notifier.SendTestAsync(cancellationToken);
        return new NotificationTestResource(result.Delivered, result.Delivered ? null : result.Reason);
    }

    /// <summary>The latest mails Portarr tried to send (50 at most), newest first, delivered or not.</summary>
    /// <response code="200">The log; a failed mail says why in a sentence, never the server's text.</response>
    [HttpGet("log")]
    [ProducesResponseType<NotificationLogResource>(StatusCodes.Status200OK)]
    public async Task<NotificationLogResource> GetLogAsync(CancellationToken cancellationToken)
    {
        var entries = await _store.LatestAsync(LogSize, cancellationToken);
        return new NotificationLogResource([.. entries.Select(entry => entry.ToResource())]);
    }

    private async Task<NotificationSettingsResource> ReadAsync(NotificationSettingsState state, CancellationToken cancellationToken)
    {
        var options = state.Options;
        var last = (await _store.LatestAsync(1, cancellationToken)).FirstOrDefault();
        var smtp = options.Smtp;
        return new NotificationSettingsResource(
            options.Enabled,
            options.Recipient,
            options.From,
            state.Policy.MaxPerDay,
            new NotificationConnectionResource(options.Provider, smtp.Host, smtp.Port, smtp.UserName, smtp.StartTls, !string.IsNullOrWhiteSpace(smtp.PasswordFile)),
            options.Enabled ? _settings.Check(options) : null,
            _settings.CheckSecrets(options),
            last?.ToResource(),
            [.. _categories.All.Select(category => category.ToResource(state.Categories[category.Id]))]);
    }
}
