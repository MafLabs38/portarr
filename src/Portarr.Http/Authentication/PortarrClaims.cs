using System.Globalization;
using System.Security.Claims;
using Portarr.Core.Accounts;

namespace Portarr.Http.Authentication;

/// <summary>How the request proved who it is: the claim <see cref="PortarrClaims.Method"/> holds one of these.</summary>
public static class AuthMethods
{
    /// <summary>The browser's cookie session.</summary>
    public const string Cookie = "cookie";

    /// <summary>A per-user API key (<c>ptr_…</c>).</summary>
    public const string ApiKey = "apikey";

    /// <summary>One of the deprecated environment keys: the built-in <c>system</c> principal.</summary>
    public const string System = "system";

    /// <summary>The short-lived token the chat runner uses on <c>/mcp</c> (<c>ptc_…</c>).</summary>
    public const string ChatToken = "chat-token";
}

/// <summary>The claims of a Portarr principal and how to read them back.</summary>
public static class PortarrClaims
{
    public const string Permission = "portarr:permission";
    public const string Method = "portarr:method";
    public const string Stamp = "portarr:stamp";
    public const string DisplayName = "portarr:display-name";
    public const string Conversation = "portarr:conversation";
    public const string Avatar = "portarr:avatar";

    /// <summary>The principal of an identity: permissions as claims, so the authorization policies are plain claim checks.</summary>
    public static ClaimsPrincipal CreatePrincipal(AccountIdentity identity, string method, string scheme, string? conversationId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId?.ToString(CultureInfo.InvariantCulture) ?? AccountIdentity.SystemUsername),
            new(ClaimTypes.Name, identity.Username),
            new(DisplayName, identity.DisplayName),
            new(ClaimTypes.Role, identity.Role),
            new(Method, method),
            new(Stamp, identity.SecurityStamp),
        };
        claims.AddRange(Permissions.Ordered(identity.Permissions).Select(p => new Claim(Permission, p)));
        if (identity.AvatarFile is not null)
        {
            claims.Add(new Claim(Avatar, identity.AvatarFile));
        }

        if (conversationId is not null)
        {
            claims.Add(new Claim(Conversation, conversationId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>The principal a session cookie carries: just enough to find the account again (the rest is read at every request).</summary>
    public static ClaimsPrincipal CreateSessionPrincipal(AccountIdentity identity, string scheme)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, identity.UserId!.Value.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, identity.Username),
            new Claim(Stamp, identity.SecurityStamp),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    public static int? UserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    public static string AuthMethod(this ClaimsPrincipal user) => user.FindFirstValue(Method) ?? string.Empty;

    public static bool HasPermission(this ClaimsPrincipal user, string permission) => user.HasClaim(Permission, permission);

    public static IReadOnlyList<string> PermissionsOf(this ClaimsPrincipal user) => Permissions.Ordered(user.FindAll(Permission).Select(c => c.Value));

    /// <summary>The actor of an account operation: who, and from where.</summary>
    public static AccountActor ToActor(this ClaimsPrincipal user, string? client) =>
        new(user.Identity?.Name ?? "-", user.UserId(), client);
}
