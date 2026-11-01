using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Portarr.Http.Authentication;

namespace Portarr.Mcp;

/// <summary>
/// The permissions a tool or a resource needs (V7, D-98), declared where it is defined: the tools run as the key's owner, so
/// each asks for the permissions of the page it mirrors (<c>apps.view</c> for the apps, <c>catalog.write</c> for any
/// change). One filter (<see cref="McpAccess"/>) enforces it, and a tool or resource with no such attribute is denied,
/// so a new one cannot be forgotten. With no permission listed, any valid key is enough, said out loud.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresPermissionsAttribute : Attribute
{
    public RequiresPermissionsAttribute(params string[] permissions)
    {
        Permissions = permissions;
    }

    public IReadOnlyList<string> Permissions { get; }
}

/// <summary>
/// What an MCP call may do (V7, D-98). The endpoint only checks that the key is valid; a key never exceeds its owner's
/// role. The filters hide what the caller may not use from the lists and refuse it when called.
/// </summary>
internal static class McpAccess
{
    /// <summary>The permissions a primitive declares, or null when it declares nothing (denied).</summary>
    public static IReadOnlyList<string>? Declared(IReadOnlyList<object>? metadata)
    {
        var declared = metadata?.OfType<RequiresPermissionsAttribute>().ToList();
        return declared is { Count: > 0 } ? declared.SelectMany(a => a.Permissions).Distinct(StringComparer.Ordinal).ToList() : null;
    }

    /// <summary>Whether the caller holds every permission the primitive declares; false when it declares none.</summary>
    public static bool Allows(HttpContext? context, IReadOnlyList<object>? metadata) =>
        Declared(metadata) is { } needed && needed.All(p => context?.User.HasPermission(p) == true);

    /// <summary>Throws an MCP error naming what is missing when the caller may not use the primitive.</summary>
    public static void Require(HttpContext? context, string name, IReadOnlyList<object>? metadata)
    {
        if (Declared(metadata) is not { } needed)
        {
            throw new McpException($"Not allowed: '{name}' declares no permission, so it is denied.");
        }

        var missing = needed.Where(p => context?.User.HasPermission(p) != true).ToList();
        if (missing.Count > 0)
        {
            throw new McpException($"Not allowed: the role behind this key lacks {string.Join(", ", missing)}.");
        }
    }

    /// <summary>The filters that enforce the declarations, for <c>WithRequestFilters</c>.</summary>
    public static void Configure(IMcpRequestFilterBuilder filters)
    {
        filters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is { } primitive)
            {
                Require(Http(context.Services), primitive.Id, primitive.Metadata);
            }

            return await next(context, cancellationToken);
        });
        filters.AddReadResourceFilter(next => async (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is { } primitive)
            {
                Require(Http(context.Services), primitive.Id, primitive.Metadata);
            }

            return await next(context, cancellationToken);
        });
        filters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            var http = Http(context.Services);
            var allowed = context.Server.ServerOptions.ToolCollection?.Where(t => Allows(http, t.Metadata)).Select(t => t.ProtocolTool.Name).ToHashSet(StringComparer.Ordinal) ?? [];
            result.Tools = result.Tools.Where(t => allowed.Contains(t.Name)).ToList();
            return result;
        });
        filters.AddListResourcesFilter(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            var http = Http(context.Services);
            var allowed = context.Server.ServerOptions.ResourceCollection?.Where(r => Allows(http, r.Metadata)).Select(r => r.ProtocolResource?.Uri).ToHashSet(StringComparer.Ordinal) ?? [];
            result.Resources = result.Resources.Where(r => allowed.Contains(r.Uri)).ToList();
            return result;
        });
    }

    private static HttpContext? Http(IServiceProvider? services) => services?.GetService(typeof(IHttpContextAccessor)) is IHttpContextAccessor accessor ? accessor.HttpContext : null;
}
