using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using Portarr.Http.Authentication;

namespace Portarr.Mcp;

/// <summary>
/// What an MCP call may do (V7, D-98): the tools run as the key's owner, so each one asks for the permissions of the page
/// it mirrors (<c>apps.view</c> for the apps, <c>catalog.write</c> for any change). The endpoint only checks that the key
/// is valid; a key never exceeds its owner's role.
/// </summary>
internal static class McpAccess
{
    /// <summary>Throws an MCP error naming what is missing when the caller lacks any of the permissions.</summary>
    public static void Require(IHttpContextAccessor accessor, params string[] permissions)
    {
        var user = accessor.HttpContext?.User;
        var missing = permissions.Where(p => user?.HasPermission(p) != true).ToList();
        if (missing.Count > 0)
        {
            throw new McpException($"Not allowed: the role behind this key lacks {string.Join(", ", missing)}.");
        }
    }
}
