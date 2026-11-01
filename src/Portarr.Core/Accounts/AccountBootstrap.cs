using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Portarr.Core.Accounts;

public interface IAccountBootstrap
{
    /// <summary>The unattended first run: creates the administrator named by <c>PORTARR__AUTH__ADMINUSER</c> when no user exists. Does nothing otherwise.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The optional unattended first run (F-SET-07, partial): the user name from <c>PORTARR__AUTH__ADMINUSER</c> and the
/// password from the file <c>PORTARR__AUTH__ADMINPASSWORDFILE</c> (a secret mount, never an environment variable),
/// read once and only while the users table is empty. Without them the first visitor sees the setup screen.
/// </summary>
public sealed partial class AccountBootstrap : IAccountBootstrap
{
    private readonly IAccountService _accounts;
    private readonly ISetupCode _setupCode;
    private readonly AuthOptions _options;
    private readonly ILogger<AccountBootstrap> _logger;

    public AccountBootstrap(IAccountService accounts, ISetupCode setupCode, IOptions<AuthOptions> options, ILogger<AccountBootstrap> logger)
    {
        _accounts = accounts;
        _setupCode = setupCode;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (await _accounts.SetupRequiredAsync(cancellationToken) && !await RunUnattendedAsync(cancellationToken))
        {
            // Nobody was created: the setup screen is what remains, and it asks for a code.
            _setupCode.Issue();
        }
    }

    /// <summary>Whether the administrator was created from the environment.</summary>
    private async Task<bool> RunUnattendedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AdminUser) || string.IsNullOrWhiteSpace(_options.AdminPasswordFile))
        {
            return false;
        }

        string password;
        try
        {
            password = (await File.ReadAllTextAsync(_options.AdminPasswordFile, cancellationToken)).TrimEnd('\r', '\n');
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(exception.GetType().Name);
            return false;
        }

        var result = await _accounts.SetupAsync(_options.AdminUser, null, password, "startup", cancellationToken);
        if (!result.Succeeded)
        {
            LogRefused(result.Message ?? "refused");
        }

        return result.Succeeded;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The unattended first run could not read the password file ({Error}): the setup screen stays available")]
    private partial void LogUnreadable(string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The unattended first run was refused: {Reason}")]
    private partial void LogRefused(string reason);
}
