using Portarr.Core.Catalog;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Api.V1.Mcp;

/// <summary>
/// An MCP server of the catalog and its health. No secret: a header or variable is a literal or the name of a variable on the
/// workstation. <c>Status</c> is healthy, authentication-required, down, waiting (no result yet) or not-checked (a local process, or check: false).
/// </summary>
public sealed record McpServerResource(
    string Id,
    string Name,
    string Transport,
    string Scope,
    string? Url,
    string? Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<McpValueResource> Values,
    string? App,
    string? CheckInterval,
    string Status,
    McpHealthResource? Health,
    bool UnpinnedPackage,
    string? Notes);

/// <summary>A header (remote) or an environment entry (local): a literal, or the variable that holds it on the workstation.</summary>
/// <summary>A header (remote) or an environment entry (local): a literal, or the variable that holds it on the workstation, with the key of the secret in the operator's store when the catalog names one.</summary>
public sealed record McpValueResource(string Kind, string Name, string? Literal, string? Variable, string? Secret = null);

/// <summary>Since when the server has been up or down, and when it was last seen healthy.</summary>
public sealed record McpHealthResource(string State, DateTimeOffset Since, DateTimeOffset? LastHealthyAt, DateTimeOffset LastCheckedAt, string? Error, int? StatusCode);

/// <summary>A client Portarr can render a server for, with the scopes it holds servers at.</summary>
public sealed record McpClientResource(string Id, string Name, IReadOnlyList<string> Scopes);

public sealed record McpOperationResource(string Kind, string Description, string Text, string? Path);

/// <summary>How to install one server in one client.</summary>
public sealed record McpRenderingResource(
    string Server,
    string Client,
    string ClientName,
    string Scope,
    bool Supported,
    IReadOnlyList<string> RequiresEnv,
    IReadOnlyList<string> RequiresSecrets,
    IReadOnlyList<McpOperationResource> Operations,
    IReadOnlyList<string> Notes);

/// <summary>A server with its installation instructions for every client, at the operating system asked.</summary>
public sealed record McpServerDetailResource(McpServerResource Server, string Os, IReadOnlyList<McpRenderingResource> Renderings);

/// <summary>An MCP server wanted on a workstation, in these clients, at this scope (null: the server's own).</summary>
public sealed record McpWantedResource(string Server, IReadOnlyList<string> Clients, string? Scope);

/// <summary>A machine the operator works from with an assistant: its system, its assistants and the servers wanted on it.</summary>
public sealed record McpWorkstationResource(
    string Id,
    string Name,
    string Os,
    string? Environment,
    IReadOnlyList<string> Clients,
    IReadOnlyList<McpWantedResource> Wanted,
    string? Notes);

/// <summary>What to install, what to remove if present, and the variables the machine must provide, for one workstation.</summary>
public sealed record McpWorkstationPlanResource(
    McpWorkstationResource Workstation,
    string Os,
    IReadOnlyList<McpRenderingResource> Install,
    IReadOnlyList<McpRenderingResource> Remove,
    IReadOnlyList<string> RequiresEnv,
    IReadOnlyList<string> RequiresSecrets);

internal static class McpResourceMapper
{
    public static McpWorkstationResource ToResource(this WorkstationDefinition workstation)
    {
        return new McpWorkstationResource(
            workstation.Id,
            workstation.Name,
            workstation.Os,
            workstation.EnvironmentId,
            workstation.Clients,
            workstation.Wanted.Select(w => new McpWantedResource(w.ServerId, w.Clients, w.Scope)).ToList(),
            workstation.Notes);
    }

    public static McpWorkstationPlanResource ToResource(this McpWorkstationPlan plan)
    {
        return new McpWorkstationPlanResource(
            plan.Workstation.ToResource(),
            plan.Os,
            plan.Install.Select(r => r.ToResource()).ToList(),
            plan.Remove.Select(r => r.ToResource()).ToList(),
            plan.RequiresEnv,
            plan.RequiresSecrets);
    }

    public static McpServerResource ToResource(this McpServerView view)
    {
        var server = view.Server;
        var values = (server.Http?.Headers.Select(h => (Kind: "header", h.Key, h.Value)) ?? [])
            .Concat(server.Stdio?.Env.Select(e => (Kind: "env", e.Key, e.Value)) ?? [])
            .Select(v => new McpValueResource(v.Kind, v.Key, v.Value.Literal, v.Value.EnvName, v.Value.SecretKey))
            .ToList();
        return new McpServerResource(
            server.Id,
            server.Name,
            server.Transport,
            server.Scope,
            server.Http?.Url.OriginalString,
            server.Stdio?.Command,
            server.Stdio?.Args ?? [],
            values,
            server.AppId,
            server.CheckInterval is { } interval ? interval.ToString("c", global::System.Globalization.CultureInfo.InvariantCulture) : null,
            view.Status,
            view.Health is { } h ? new McpHealthResource(h.State.ToString(), h.Since, h.LastHealthyAt, h.LastCheckedAt, h.Error, h.StatusCode) : null,
            IsUnpinned(server),
            server.Notes);
    }

    public static McpRenderingResource ToResource(this McpRendering rendering)
    {
        return new McpRenderingResource(
            rendering.ServerId,
            rendering.Client,
            rendering.ClientName,
            rendering.Scope,
            rendering.Supported,
            rendering.RequiresEnv,
            rendering.RequiresSecrets,
            rendering.Operations.Select(o => new McpOperationResource(o.Kind, o.Description, o.Text, o.Path)).ToList(),
            rendering.Notes);
    }

    /// <summary>A package run through <c>npx</c> or <c>uvx</c> without a version: it changes under the operator's feet.</summary>
    internal static bool IsUnpinned(McpServerDefinition server)
    {
        if (server.Stdio is not { Command: "npx" or "uvx" or "bunx" or "pnpx" } stdio)
        {
            return false;
        }

        var package = stdio.Args.FirstOrDefault(a => !a.StartsWith('-'));
        if (package is null)
        {
            return false;
        }

        var at = package.LastIndexOf('@');
        return at <= 0 || package.EndsWith("@latest", StringComparison.Ordinal);
    }
}
