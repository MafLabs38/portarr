using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Portarr.Core.Accounts;

/// <summary>Who does something, for the audit and for the rules about one's own account.</summary>
/// <param name="Name">The user name, or <c>system</c>.</param>
/// <param name="UserId">The account, or null for the system principal and for anonymous calls.</param>
/// <param name="Client">The address the request came from.</param>
public sealed record AccountActor(string Name, int? UserId, string? Client)
{
    public static AccountActor Anonymous(string? client) => new("-", null, client);
}

public interface IAccountService
{
    /// <summary>True while no user exists: the first run (F-SET-07).</summary>
    Task<bool> SetupRequiredAsync(CancellationToken cancellationToken);

    /// <summary>Creates the first administrator; refused as soon as a user exists, whoever asks first.</summary>
    Task<AccountResult<AccountIdentity>> SetupAsync(string username, string? displayName, string password, string? client, CancellationToken cancellationToken);

    /// <summary>Checks a password. Fails with one generic message whatever the reason (unknown user, bad password, disabled, locked).</summary>
    Task<AccountResult<AccountIdentity>> LoginAsync(string username, string password, string? client, CancellationToken cancellationToken);

    /// <summary>The identity of a session or key owner as it is now, or null if the user is gone, disabled, or (with a stamp) signed out by a password change.</summary>
    Task<AccountIdentity?> ResolveAsync(int userId, string? securityStamp, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountUser>> ListUsersAsync(CancellationToken cancellationToken);

    Task<AccountUser?> GetUserAsync(int id, CancellationToken cancellationToken);

    Task<AccountResult<AccountUser>> CreateUserAsync(AccountActor actor, string username, string? displayName, string password, int roleId, CancellationToken cancellationToken);

    /// <summary>Changes the fields given: display name, role, disabled. One's own role and state are not changeable; the last enabled Admin stays.</summary>
    Task<AccountResult<AccountUser>> UpdateUserAsync(AccountActor actor, int id, string? displayName, int? roleId, bool? disabled, CancellationToken cancellationToken);

    Task<AccountResult<AccountUser>> ResetPasswordAsync(AccountActor actor, int id, string newPassword, CancellationToken cancellationToken);

    /// <summary>The profile's password change: the current password is required. Returns the identity with its new stamp (the session is renewed with it).</summary>
    Task<AccountResult<AccountIdentity>> ChangePasswordAsync(AccountActor actor, string currentPassword, string newPassword, CancellationToken cancellationToken);

    Task<AccountResult<AccountUser>> UpdateProfileAsync(AccountActor actor, string displayName, CancellationToken cancellationToken);

    Task<AccountResult<AccountUser>> DeleteUserAsync(AccountActor actor, int id, CancellationToken cancellationToken);

    Task<AccountResult<AccountUser>> SetAvatarAsync(AccountActor actor, int id, string? avatarFile, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleDefinition>> ListRolesAsync(CancellationToken cancellationToken);

    Task<AccountResult<RoleDefinition>> CreateRoleAsync(AccountActor actor, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    Task<AccountResult<RoleDefinition>> UpdateRoleAsync(AccountActor actor, int id, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    Task<AccountResult<RoleDefinition>> DeleteRoleAsync(AccountActor actor, int id, CancellationToken cancellationToken);
}

/// <summary>
/// Local accounts and roles (V7, D-92 to D-95): the rules, over <see cref="AccountStore"/>. Passwords are hashed with
/// ASP.NET Core's <see cref="PasswordHasher{TUser}"/> (PBKDF2). Every sign-in answer is the same generic one and costs
/// one hash check, so neither the answer nor its timing says whether a user exists; five failures lock the account for
/// five minutes. Mutations that depend on a count (the last Admin) run one at a time.
/// </summary>
public sealed partial class AccountService : IAccountService
{
    public const int MinimumPasswordLength = 10;
    public const int MaximumPasswordLength = 128;
    public const string LoginFailedMessage = "Invalid username or password, or the account is temporarily locked.";

    /// <summary>How long a resolved identity may be reused: short, and cleared by every change made through this service.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);

    private static readonly PasswordHasher<string> Hasher = new();
    private static readonly Lazy<string> DummyHash = new(() => Hasher.HashPassword("dummy", Guid.NewGuid().ToString("N")));

    private readonly AccountStore _store;
    private readonly IAuthAuditLog _audit;
    private readonly AuthOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountService> _logger;
    private readonly SemaphoreSlim _mutations = new(1, 1);
    private readonly ConcurrentDictionary<int, (DateTimeOffset At, AccountIdentity? Identity)> _identities = new();
    private (DateTimeOffset At, IReadOnlyList<RoleDefinition> Roles)? _roles;

    internal AccountService(AccountStore store, IAuthAuditLog audit, IOptions<AuthOptions> options, TimeProvider timeProvider, ILogger<AccountService> logger)
    {
        _store = store;
        _audit = audit;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{2,31}$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 ._-]{0,38}[A-Za-z0-9]$")]
    private static partial Regex RoleNamePattern();

    /// <summary>Why a user name is refused, or null: 3 to 32 letters, digits, dots, dashes and underscores.</summary>
    public static string? ValidateUsername(string? username) =>
        username is not null && UsernamePattern().IsMatch(username) ? null : "username must be 3 to 32 characters: letters, digits, '.', '-' and '_', starting with a letter or a digit.";

    /// <summary>Why a password is refused, or null.</summary>
    public static string? ValidatePassword(string? password, string? username = null)
    {
        if (password is null || password.Length < MinimumPasswordLength || password.Length > MaximumPasswordLength)
        {
            return $"password must be {MinimumPasswordLength} to {MaximumPasswordLength} characters.";
        }

        return username is not null && string.Equals(password, username, StringComparison.OrdinalIgnoreCase) ? "password must not be the user name." : null;
    }

    public static string? ValidateDisplayName(string? displayName) =>
        displayName is { Length: >= 1 and <= 64 } && !displayName.Any(char.IsControl) ? null : "display name must be 1 to 64 characters on one line.";

    public async Task<bool> SetupRequiredAsync(CancellationToken cancellationToken) => await _store.CountUsersAsync(cancellationToken) == 0;

    public async Task<AccountResult<AccountIdentity>> SetupAsync(string username, string? displayName, string password, string? client, CancellationToken cancellationToken)
    {
        var name = username.Trim();
        var display = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        if ((ValidateUsername(name) ?? ValidateDisplayName(display) ?? ValidatePassword(password, name)) is { } invalid)
        {
            return AccountResult<AccountIdentity>.Fail(AccountError.Invalid, invalid);
        }

        await _mutations.WaitAsync(cancellationToken);
        try
        {
            var admin = (await RolesAsync(cancellationToken)).First(r => r.BuiltIn && r.Name == BuiltInRoles.Admin);
            var id = await _store.TryCreateFirstAdminAsync(new NewUser(name, display, Hasher.HashPassword(name, password), admin.Id, _timeProvider.GetUtcNow()), cancellationToken);
            if (id is null)
            {
                return AccountResult<AccountIdentity>.Fail(AccountError.Conflict, "Portarr is already set up.");
            }

            Invalidate();
            await _audit.AppendAsync("setup", name, name, "first administrator created", client, cancellationToken);
            LogSetup(name);
            return AccountResult<AccountIdentity>.Ok((await ResolveAsync(id.Value, null, cancellationToken))!);
        }
        finally
        {
            _mutations.Release();
        }
    }

    public async Task<AccountResult<AccountIdentity>> LoginAsync(string username, string password, string? client, CancellationToken cancellationToken)
    {
        var failed = AccountResult<AccountIdentity>.Fail(AccountError.Forbidden, LoginFailedMessage);
        var row = username.Length is > 0 and <= 64 ? await _store.FindByUsernameAsync(username, cancellationToken) : null;
        var now = _timeProvider.GetUtcNow();

        // One hash check whatever happens, so timing does not tell an unknown user from a wrong password.
        var tooLong = password.Length > MaximumPasswordLength;
        var verdict = Hasher.VerifyHashedPassword(username, row?.PasswordHash ?? DummyHash.Value, tooLong ? string.Empty : password);
        verdict = tooLong ? PasswordVerificationResult.Failed : verdict;
        var usable = row is { Disabled: false } && (Iso.ParseOrNull(row.LockoutEnd) is not { } until || until <= now);

        if (row is null || !usable || verdict == PasswordVerificationResult.Failed)
        {
            await RecordFailureAsync(row, usable, username, client, now, cancellationToken);
            return failed;
        }

        await _store.RecordLoginAsync(row.Id, now, cancellationToken);
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            await _store.SetPasswordHashOnlyAsync(row.Id, Hasher.HashPassword(row.Username, password), cancellationToken);
        }

        Invalidate();
        await _audit.AppendAsync("login.success", row.Username, row.Username, null, client, cancellationToken);
        var identity = await ResolveAsync(row.Id, null, cancellationToken);
        return identity is null ? failed : AccountResult<AccountIdentity>.Ok(identity);
    }

    public async Task<AccountIdentity?> ResolveAsync(int userId, string? securityStamp, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (!_identities.TryGetValue(userId, out var cached) || now - cached.At > CacheLifetime)
        {
            var row = await _store.FindByIdAsync(userId, cancellationToken);
            var identity = row is null || row.Disabled ? null : await BuildIdentityAsync(row, cancellationToken);
            cached = (now, identity);
            _identities[userId] = cached;
        }

        return cached.Identity is { } found && (securityStamp is null || string.Equals(found.SecurityStamp, securityStamp, StringComparison.Ordinal)) ? found : null;
    }

    public async Task<IReadOnlyList<AccountUser>> ListUsersAsync(CancellationToken cancellationToken) =>
        (await _store.ListAsync(cancellationToken)).Select(r => r.ToUser()).ToList();

    public async Task<AccountUser?> GetUserAsync(int id, CancellationToken cancellationToken) => (await _store.FindByIdAsync(id, cancellationToken))?.ToUser();

    public async Task<AccountResult<AccountUser>> CreateUserAsync(AccountActor actor, string username, string? displayName, string password, int roleId, CancellationToken cancellationToken)
    {
        var name = username.Trim();
        var display = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
        if ((ValidateUsername(name) ?? ValidateDisplayName(display) ?? ValidatePassword(password, name)) is { } invalid)
        {
            return AccountResult<AccountUser>.Fail(AccountError.Invalid, invalid);
        }

        await _mutations.WaitAsync(cancellationToken);
        try
        {
            if ((await RolesAsync(cancellationToken)).All(r => r.Id != roleId))
            {
                return AccountResult<AccountUser>.Fail(AccountError.Invalid, "role does not exist.");
            }

            if (await _store.FindByUsernameAsync(name, cancellationToken) is not null)
            {
                return AccountResult<AccountUser>.Fail(AccountError.Conflict, "That user name is taken.");
            }

            var id = await _store.InsertUserAsync(new NewUser(name, display, Hasher.HashPassword(name, password), roleId, _timeProvider.GetUtcNow()), cancellationToken);
            Invalidate();
            await _audit.AppendAsync("user.create", actor.Name, name, $"role {RoleName(roleId)}", actor.Client, cancellationToken);
            return AccountResult<AccountUser>.Ok((await GetUserAsync(id, cancellationToken))!);
        }
        finally
        {
            _mutations.Release();
        }
    }

    public async Task<AccountResult<AccountUser>> UpdateUserAsync(AccountActor actor, int id, string? displayName, int? roleId, bool? disabled, CancellationToken cancellationToken)
    {
        if (displayName is not null && ValidateDisplayName(displayName.Trim()) is { } invalid)
        {
            return AccountResult<AccountUser>.Fail(AccountError.Invalid, invalid);
        }

        await _mutations.WaitAsync(cancellationToken);
        try
        {
            var row = await _store.FindByIdAsync(id, cancellationToken);
            if (row is null)
            {
                return AccountResult<AccountUser>.Fail(AccountError.NotFound, "No such user.");
            }

            var self = actor.UserId == id;
            var changesRole = roleId is { } r && r != row.RoleId;
            var changesState = disabled is { } d && d != row.Disabled;
            if (self && (changesRole || changesState))
            {
                return AccountResult<AccountUser>.Fail(AccountError.Forbidden, "You cannot change your own role or disable your own account.");
            }

            if (changesRole && (await RolesAsync(cancellationToken)).All(role => role.Id != roleId))
            {
                return AccountResult<AccountUser>.Fail(AccountError.Invalid, "role does not exist.");
            }

            var leavesAdmin = row is { Disabled: false } && row.RoleName == BuiltInRoles.Admin && ((changesState && disabled == true) || changesRole);
            if (leavesAdmin && await _store.CountEnabledAdminsAsync(id, cancellationToken) == 0)
            {
                return AccountResult<AccountUser>.Fail(AccountError.Conflict, "At least one enabled Admin must remain.");
            }

            if (displayName is not null && displayName.Trim() != row.DisplayName)
            {
                await _store.SetDisplayNameAsync(id, displayName.Trim(), cancellationToken);
                await _audit.AppendAsync("user.update", actor.Name, row.Username, "display name changed", actor.Client, cancellationToken);
            }

            if (changesRole)
            {
                await _store.SetRoleAsync(id, roleId!.Value, cancellationToken);
                await _audit.AppendAsync("user.role", actor.Name, row.Username, $"role {row.RoleName} to {RoleName(roleId.Value)}", actor.Client, cancellationToken);
            }

            if (changesState)
            {
                await _store.SetDisabledAsync(id, disabled!.Value, AccountStore.NewStamp(), cancellationToken);
                await _audit.AppendAsync(disabled.Value ? "user.disable" : "user.enable", actor.Name, row.Username, null, actor.Client, cancellationToken);
            }

            Invalidate();
            return AccountResult<AccountUser>.Ok((await GetUserAsync(id, cancellationToken))!);
        }
        finally
        {
            _mutations.Release();
        }
    }

    public async Task<AccountResult<AccountUser>> ResetPasswordAsync(AccountActor actor, int id, string newPassword, CancellationToken cancellationToken)
    {
        var row = await _store.FindByIdAsync(id, cancellationToken);
        if (row is null)
        {
            return AccountResult<AccountUser>.Fail(AccountError.NotFound, "No such user.");
        }

        if (ValidatePassword(newPassword, row.Username) is { } invalid)
        {
            return AccountResult<AccountUser>.Fail(AccountError.Invalid, invalid);
        }

        await _store.SetPasswordAsync(id, Hasher.HashPassword(row.Username, newPassword), AccountStore.NewStamp(), cancellationToken);
        Invalidate();
        await _audit.AppendAsync("user.password-reset", actor.Name, row.Username, null, actor.Client, cancellationToken);
        return AccountResult<AccountUser>.Ok((await GetUserAsync(id, cancellationToken))!);
    }

    public async Task<AccountResult<AccountIdentity>> ChangePasswordAsync(AccountActor actor, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var row = actor.UserId is { } userId ? await _store.FindByIdAsync(userId, cancellationToken) : null;
        if (row is null)
        {
            return AccountResult<AccountIdentity>.Fail(AccountError.NotFound, "No such user.");
        }

        if (ValidatePassword(newPassword, row.Username) is { } invalid)
        {
            return AccountResult<AccountIdentity>.Fail(AccountError.Invalid, invalid);
        }

        if (Hasher.VerifyHashedPassword(row.Username, row.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            await _audit.AppendAsync("password.failure", row.Username, row.Username, "current password refused", actor.Client, cancellationToken);
            return AccountResult<AccountIdentity>.Fail(AccountError.Forbidden, "The current password is not right.");
        }

        await _store.SetPasswordAsync(row.Id, Hasher.HashPassword(row.Username, newPassword), AccountStore.NewStamp(), cancellationToken);
        Invalidate();
        await _audit.AppendAsync("password.change", row.Username, row.Username, null, actor.Client, cancellationToken);
        return AccountResult<AccountIdentity>.Ok((await ResolveAsync(row.Id, null, cancellationToken))!);
    }

    public async Task<AccountResult<AccountUser>> UpdateProfileAsync(AccountActor actor, string displayName, CancellationToken cancellationToken)
    {
        var display = displayName.Trim();
        if (ValidateDisplayName(display) is { } invalid)
        {
            return AccountResult<AccountUser>.Fail(AccountError.Invalid, invalid);
        }

        if (actor.UserId is not { } id || await _store.FindByIdAsync(id, cancellationToken) is null)
        {
            return AccountResult<AccountUser>.Fail(AccountError.NotFound, "No such user.");
        }

        await _store.SetDisplayNameAsync(id, display, cancellationToken);
        Invalidate();
        await _audit.AppendAsync("profile.update", actor.Name, actor.Name, "display name changed", actor.Client, cancellationToken);
        return AccountResult<AccountUser>.Ok((await GetUserAsync(id, cancellationToken))!);
    }

    public async Task<AccountResult<AccountUser>> DeleteUserAsync(AccountActor actor, int id, CancellationToken cancellationToken)
    {
        await _mutations.WaitAsync(cancellationToken);
        try
        {
            var row = await _store.FindByIdAsync(id, cancellationToken);
            if (row is null)
            {
                return AccountResult<AccountUser>.Fail(AccountError.NotFound, "No such user.");
            }

            if (actor.UserId == id)
            {
                return AccountResult<AccountUser>.Fail(AccountError.Forbidden, "You cannot delete your own account.");
            }

            if (row is { Disabled: false } && row.RoleName == BuiltInRoles.Admin && await _store.CountEnabledAdminsAsync(id, cancellationToken) == 0)
            {
                return AccountResult<AccountUser>.Fail(AccountError.Conflict, "At least one enabled Admin must remain.");
            }

            await _store.DeleteAsync(id, cancellationToken);
            Invalidate();
            await _audit.AppendAsync("user.delete", actor.Name, row.Username, null, actor.Client, cancellationToken);
            return AccountResult<AccountUser>.Ok(row.ToUser());
        }
        finally
        {
            _mutations.Release();
        }
    }

    public async Task<AccountResult<AccountUser>> SetAvatarAsync(AccountActor actor, int id, string? avatarFile, CancellationToken cancellationToken)
    {
        var row = await _store.FindByIdAsync(id, cancellationToken);
        if (row is null)
        {
            return AccountResult<AccountUser>.Fail(AccountError.NotFound, "No such user.");
        }

        await _store.SetAvatarAsync(id, avatarFile, cancellationToken);
        Invalidate();
        await _audit.AppendAsync("avatar.update", actor.Name, row.Username, avatarFile is null ? "removed" : "changed", actor.Client, cancellationToken);
        return AccountResult<AccountUser>.Ok((await GetUserAsync(id, cancellationToken))!);
    }

    public Task<IReadOnlyList<RoleDefinition>> ListRolesAsync(CancellationToken cancellationToken) => _store.ListRolesAsync(cancellationToken);

    public async Task<AccountResult<RoleDefinition>> CreateRoleAsync(AccountActor actor, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        await _mutations.WaitAsync(cancellationToken);
        try
        {
            var roles = await _store.ListRolesAsync(cancellationToken);
            if (ValidateRole(name, description, permissions, roles, null) is { } invalid)
            {
                return invalid;
            }

            var id = await _store.InsertRoleAsync(name.Trim(), Clean(description), permissions, _timeProvider.GetUtcNow(), cancellationToken);
            Invalidate();
            await _audit.AppendAsync("role.create", actor.Name, name.Trim(), $"{permissions.Distinct().Count()} permissions", actor.Client, cancellationToken);
            return AccountResult<RoleDefinition>.Ok((await _store.ListRolesAsync(cancellationToken)).First(r => r.Id == id));
        }
        finally
        {
            _mutations.Release();
        }
    }

    public async Task<AccountResult<RoleDefinition>> UpdateRoleAsync(AccountActor actor, int id, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        await _mutations.WaitAsync(cancellationToken);
        try
        {
            var roles = await _store.ListRolesAsync(cancellationToken);
            var role = roles.FirstOrDefault(r => r.Id == id);
            if (role is null)
            {
                return AccountResult<RoleDefinition>.Fail(AccountError.NotFound, "No such role.");
            }

            if (role.BuiltIn)
            {
                return AccountResult<RoleDefinition>.Fail(AccountError.Forbidden, $"{role.Name} is a built-in role: create a custom role to vary it.");
            }

            if (ValidateRole(name, description, permissions, roles, id) is { } invalid)
            {
                return invalid;
            }

            await _store.UpdateRoleAsync(id, name.Trim(), Clean(description), permissions, cancellationToken);
            Invalidate();
            await _audit.AppendAsync("role.update", actor.Name, name.Trim(), $"{permissions.Distinct().Count()} permissions", actor.Client, cancellationToken);
            return AccountResult<RoleDefinition>.Ok((await _store.ListRolesAsync(cancellationToken)).First(r => r.Id == id));
        }
        finally
        {
            _mutations.Release();
        }
    }

    public async Task<AccountResult<RoleDefinition>> DeleteRoleAsync(AccountActor actor, int id, CancellationToken cancellationToken)
    {
        await _mutations.WaitAsync(cancellationToken);
        try
        {
            var role = (await _store.ListRolesAsync(cancellationToken)).FirstOrDefault(r => r.Id == id);
            if (role is null)
            {
                return AccountResult<RoleDefinition>.Fail(AccountError.NotFound, "No such role.");
            }

            if (role.BuiltIn)
            {
                return AccountResult<RoleDefinition>.Fail(AccountError.Forbidden, $"{role.Name} is a built-in role and cannot be deleted.");
            }

            if (role.Users > 0)
            {
                return AccountResult<RoleDefinition>.Fail(AccountError.Conflict, $"{role.Users} user(s) still have this role: give them another one first.");
            }

            await _store.DeleteRoleAsync(id, cancellationToken);
            Invalidate();
            await _audit.AppendAsync("role.delete", actor.Name, role.Name, null, actor.Client, cancellationToken);
            return AccountResult<RoleDefinition>.Ok(role);
        }
        finally
        {
            _mutations.Release();
        }
    }

    private static AccountResult<RoleDefinition>? ValidateRole(string name, string? description, IReadOnlyCollection<string> permissions, IReadOnlyList<RoleDefinition> roles, int? ownId)
    {
        if (!RoleNamePattern().IsMatch(name.Trim()))
        {
            return AccountResult<RoleDefinition>.Fail(AccountError.Invalid, "name must be 2 to 40 characters: letters, digits, spaces, '.', '-' and '_'.");
        }

        if (description is { Length: > 200 } || description?.Any(char.IsControl) == true)
        {
            return AccountResult<RoleDefinition>.Fail(AccountError.Invalid, "description must be one line of 200 characters at most.");
        }

        if (permissions.Count == 0 || permissions.Any(p => !Permissions.Assignable.Contains(p)))
        {
            return AccountResult<RoleDefinition>.Fail(AccountError.Invalid, "permissions must be a non-empty list of known permissions.");
        }

        return roles.Any(r => r.Id != ownId && string.Equals(r.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ? AccountResult<RoleDefinition>.Fail(AccountError.Conflict, "A role with that name exists.")
            : null;
    }

    private static string? Clean(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static string FailureDetail(UserRow? row, bool usable)
    {
        if (row is null)
        {
            return "unknown user";
        }

        if (row.Disabled)
        {
            return "account disabled";
        }

        return usable ? "wrong password" : "account locked";
    }

    private async Task RecordFailureAsync(UserRow? row, bool usable, string typed, string? client, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var detail = FailureDetail(row, usable);
        if (row is not null && usable)
        {
            var attempts = await _store.IncrementFailuresAsync(row.Id, cancellationToken);
            if (attempts >= _options.MaxFailedAttempts)
            {
                await _store.LockAsync(row.Id, now + _options.Lockout, cancellationToken);
                detail = $"wrong password; locked for {_options.LockoutMinutes} minutes";
            }
        }

        await _audit.AppendAsync("login.failure", typed, row?.Username, detail, client, cancellationToken);
        LogLoginFailed(client ?? "unknown");
    }

    private async Task<AccountIdentity> BuildIdentityAsync(UserRow row, CancellationToken cancellationToken)
    {
        var role = (await RolesAsync(cancellationToken)).FirstOrDefault(r => r.Id == row.RoleId);
        var permissions = role is null ? new HashSet<string>() : role.Permissions.ToHashSet(StringComparer.Ordinal);
        return new AccountIdentity(row.Id, row.Username, row.DisplayName, row.RoleName, permissions, row.SecurityStamp, row.AvatarFile);
    }

    private async Task<IReadOnlyList<RoleDefinition>> RolesAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_roles is { } cached && now - cached.At <= CacheLifetime)
        {
            return cached.Roles;
        }

        var roles = await _store.ListRolesAsync(cancellationToken);
        _roles = (now, roles);
        return roles;
    }

    private string RoleName(int roleId) => _roles?.Roles.FirstOrDefault(r => r.Id == roleId)?.Name ?? roleId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void Invalidate()
    {
        _identities.Clear();
        _roles = null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in failed from {Client}")]
    private partial void LogLoginFailed(string client);

    [LoggerMessage(Level = LogLevel.Information, Message = "First administrator {User} created")]
    private partial void LogSetup(string user);
}
