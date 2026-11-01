namespace Portarr.Core.Accounts;

/// <summary>A user as the API and the UI know it. Never carries the password hash.</summary>
public sealed record AccountUser(
    int Id,
    string Username,
    string DisplayName,
    int RoleId,
    string Role,
    string? AvatarFile,
    DateTimeOffset Created,
    DateTimeOffset? LastLogin,
    bool Disabled,
    DateTimeOffset? LockoutEnd);

/// <summary>A role: a named set of permissions. The built-in ones are defined in code (<see cref="BuiltInRoles"/>).</summary>
public sealed record RoleDefinition(int Id, string Name, string? Description, bool BuiltIn, IReadOnlyList<string> Permissions, int Users);

/// <summary>
/// Who a request is, once authenticated: an account (cookie session or its API key), a chat token, or the built-in
/// <c>system</c> principal of the deprecated environment keys. <see cref="Permissions"/> is read from the role at
/// every request, so editing a role takes effect at once.
/// </summary>
/// <param name="UserId">The account, or null for the system principal.</param>
/// <param name="Username">The login name; <c>system</c> for the environment keys.</param>
/// <param name="DisplayName">What the UI shows.</param>
/// <param name="Role">The role's name.</param>
/// <param name="Permissions">What the principal may do (never more than its owner's role).</param>
/// <param name="SecurityStamp">Changes with the password: older sessions stop working.</param>
/// <param name="AvatarFile">The avatar's file name, if any.</param>
public sealed record AccountIdentity(
    int? UserId,
    string Username,
    string DisplayName,
    string Role,
    IReadOnlySet<string> Permissions,
    string SecurityStamp,
    string? AvatarFile)
{
    /// <summary>The name of the principal of the environment keys.</summary>
    public const string SystemUsername = "system";

    public bool Has(string permission) => Permissions.Contains(permission);

    /// <summary>The principal of an environment key, holding the permissions that key's source declares and nothing else.</summary>
    public static AccountIdentity ForSystem(IReadOnlySet<string> permissions) => new(null, SystemUsername, "System", "System", permissions, string.Empty, null);
}

public enum AccountError
{
    None,

    /// <summary>The input is not valid (message tells which field).</summary>
    Invalid,

    /// <summary>The name is taken, or the change would break a rule (last admin, role in use).</summary>
    Conflict,

    NotFound,

    /// <summary>The caller may not do this to this target (own role, own account, built-in role).</summary>
    Forbidden,
}

/// <summary>The outcome of an account operation: a value, or an error with a message safe to show.</summary>
public readonly record struct AccountResult<T>(T? Value, AccountError Error = AccountError.None, string? Message = null)
{
    public bool Succeeded => Error == AccountError.None;

    public static AccountResult<T> Ok(T value) => new(value);

    public static AccountResult<T> Fail(AccountError error, string message) => new(default, error, message);
}

/// <summary>One line of the auth audit (V7, D-96): never a password, a key or a token.</summary>
public sealed record AuthAuditEntry(DateTimeOffset At, string Event, string? Actor, string? Subject, string? Detail, string? Client);
