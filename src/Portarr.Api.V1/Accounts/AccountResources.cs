using System.Security.Claims;
using Portarr.Core.Accounts;
using Portarr.Http.Authentication;

namespace Portarr.Api.V1.Accounts;

/// <summary>
/// Who is signed in (V7): the account, its role, the permissions the UI shows pages for, and how the request authenticated
/// (<c>cookie</c> for a browser session, <c>apikey</c>, <c>system</c>). <c>Id</c> is null for the environment keys' principal.
/// </summary>
public sealed record MeResource(int? Id, string Username, string DisplayName, string Role, IReadOnlyList<string> Permissions, string? AvatarUrl, string AuthMethod);

/// <summary>Whether the first administrator still has to be created, and what the login page shows of the portal (its name and logo).</summary>
public sealed record AuthStatusResource(bool SetupRequired, string Name, string? LogoUrl, bool SetupCodeRequired);

/// <summary><c>SetupCode</c> is the one-time code printed in the Portarr log (and in <c>&lt;data&gt;/setup-code</c>) on a fresh install.</summary>
public sealed record SetupRequest(string Username, string? DisplayName, string Password, string? SetupCode = null);

public sealed record LoginRequest(string Username, string Password, bool Remember = false);

public sealed record ProfileRequest(string DisplayName);

public sealed record PasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>An API key as listed: its prefix, scope and dates, never the key.</summary>
public sealed record ApiKeyResource(int Id, string Name, string Prefix, string Scope, DateTimeOffset Created, DateTimeOffset? LastUsed, DateTimeOffset? Expires, DateTimeOffset? Revoked, bool Active);

/// <summary>A key just created: <c>Value</c> is shown this once and cannot be read back.</summary>
public sealed record CreatedApiKeyResource(ApiKeyResource Key, string Value);

/// <summary><c>Scope</c> is <c>read</c> (the view permissions of your role) or <c>full</c> (all of your permissions).</summary>
public sealed record ApiKeyRequest(string Name, string Scope, DateTimeOffset? Expires);

public sealed record UserResource(
    int Id,
    string Username,
    string DisplayName,
    int RoleId,
    string Role,
    string? AvatarUrl,
    DateTimeOffset Created,
    DateTimeOffset? LastLogin,
    bool Disabled,
    DateTimeOffset? LockedUntil);

public sealed record UserCreateRequest(string Username, string? DisplayName, string Password, [property: global::System.Text.Json.Serialization.JsonRequired] int RoleId);

/// <summary>Only what is given changes.</summary>
public sealed record UserUpdateRequest(string? DisplayName, int? RoleId, bool? Disabled);

public sealed record PasswordResetRequest(string Password);

public sealed record RoleResource(int Id, string Name, string? Description, bool BuiltIn, IReadOnlyList<string> Permissions, int Users);

public sealed record RoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record PermissionResource(string Key, string Area, bool Reserved);

/// <summary>The roles and the closed list of permissions a role is made of.</summary>
public sealed record RoleListResource(IReadOnlyList<RoleResource> Roles, IReadOnlyList<PermissionResource> Permissions);

public sealed record AuthActivityResource(DateTimeOffset At, string Event, string? Actor, string? Subject, string? Detail, string? Client);

public static class AccountResourceMapper
{
    public const string AvatarPath = "/api/v1/avatars";

    /// <summary>The URL of an avatar, with the image's hash so a new image is fetched at once and an old one cached for a year.</summary>
    public static string? AvatarUrl(int? userId, string? avatarFile)
    {
        if (userId is null || string.IsNullOrEmpty(avatarFile))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(avatarFile);
        return $"{AvatarPath}/{userId}?v={stem[(stem.LastIndexOf('-') + 1)..]}";
    }

    public static MeResource ToMe(this AccountIdentity identity, string method) =>
        new(identity.UserId, identity.Username, identity.DisplayName, identity.Role, Permissions.Ordered(identity.Permissions), AvatarUrl(identity.UserId, identity.AvatarFile), method);

    public static MeResource ToMe(this ClaimsPrincipal user) =>
        new(user.UserId(), user.Identity?.Name ?? string.Empty, user.FindFirstValue(PortarrClaims.DisplayName) ?? string.Empty, user.FindFirstValue(ClaimTypes.Role) ?? string.Empty, user.PermissionsOf(), AvatarUrl(user.UserId(), user.FindFirstValue(PortarrClaims.Avatar)), user.AuthMethod());

    public static UserResource ToResource(this AccountUser user, DateTimeOffset now) =>
        new(user.Id, user.Username, user.DisplayName, user.RoleId, user.Role, AvatarUrl(user.Id, user.AvatarFile), user.Created, user.LastLogin, user.Disabled, user.LockoutEnd is { } until && until > now ? until : null);

    public static RoleResource ToResource(this RoleDefinition role) => new(role.Id, role.Name, role.Description, role.BuiltIn, role.Permissions, role.Users);

    public static ApiKeyResource ToResource(this ApiKeyInfo key, DateTimeOffset now) =>
        new(key.Id, key.Name, key.Prefix, key.Scope, key.Created, key.LastUsed, key.Expires, key.Revoked, key.IsActive(now));
}
