using Microsoft.Extensions.Options;

namespace Portarr.Core.Accounts;

/// <summary>
/// Local accounts (V7, D-92), section <c>Auth</c>: <c>PORTARR__AUTH__SESSIONDAYS</c> (sliding session, 7 by default),
/// <c>MAXFAILEDATTEMPTS</c> and <c>LOCKOUTMINUTES</c> (5 failures lock the account for 5 minutes), and the optional
/// unattended first run: <c>PORTARR__AUTH__ADMINUSER</c> with <c>PORTARR__AUTH__ADMINPASSWORDFILE</c> (a file holding
/// the password), read once, only while no user exists. <c>PORTARR__AUTH__COOKIESECURITY=Always</c> issues the session
/// cookie as <c>Secure</c> (with the <c>__Host-</c> prefix) whatever the request looks like, for an install behind a TLS
/// proxy Portarr does not trust; the default <c>Auto</c> follows the request, so plain HTTP on a LAN keeps working.
/// A fresh install asks for a one-time setup code (printed in the log, written to <c>&lt;data&gt;/setup-code</c>) before
/// the first administrator is created; <c>PORTARR__AUTH__REQUIRESETUPCODE=false</c> removes the guard.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int SessionDays { get; set; } = 7;

    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 5;

    public CookieSecurity CookieSecurity { get; set; } = CookieSecurity.Auto;

    public bool RequireSetupCode { get; set; } = true;

    public string? AdminUser { get; set; }

    public string? AdminPasswordFile { get; set; }

    public TimeSpan Lockout => TimeSpan.FromMinutes(LockoutMinutes);
}

/// <summary>When the session cookie carries the <c>Secure</c> attribute.</summary>
public enum CookieSecurity
{
    /// <summary>When the request is HTTPS (as seen after the trusted proxies' forwarded headers).</summary>
    Auto,

    /// <summary>Always, so the browser sends the cookie over HTTPS only.</summary>
    Always,
}

/// <summary>Fails startup on a nonsensical session length or lockout, or on half of the unattended first run.</summary>
public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var errors = new List<string>();
        if (options.SessionDays is < 1 or > 365)
        {
            errors.Add("Auth:SessionDays must be between 1 and 365.");
        }

        if (options.MaxFailedAttempts is < 1 or > 100)
        {
            errors.Add("Auth:MaxFailedAttempts must be between 1 and 100.");
        }

        if (options.LockoutMinutes is < 1 or > 1440)
        {
            errors.Add("Auth:LockoutMinutes must be between 1 and 1440.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminUser) != string.IsNullOrWhiteSpace(options.AdminPasswordFile))
        {
            errors.Add("Auth:AdminUser and Auth:AdminPasswordFile go together (PORTARR__AUTH__ADMINUSER, PORTARR__AUTH__ADMINPASSWORDFILE).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
