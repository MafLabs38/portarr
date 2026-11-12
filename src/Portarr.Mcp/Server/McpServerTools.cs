using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Mcp.Server;

/// <summary>An MCP server of the catalog: where it is, whether it is up and since when. No secret.</summary>
public sealed record McpServerLine(
    string Id,
    string Name,
    string Transport,
    string Scope,
    string? Url,
    string? Command,
    string Status,
    string? Since,
    string? LastHealthyAt,
    string? LastCheckedAt,
    string? Error,
    string? App,
    string? Notes);

/// <summary>How to install, or remove, one server in one client on one operating system.</summary>
public sealed record McpInstallAnswer(
    string Server,
    string Client,
    string ClientName,
    string Scope,
    string Os,
    string Action,
    bool Supported,
    IReadOnlyList<string> RequiresEnv,
    IReadOnlyList<string> RequiresSecrets,
    IReadOnlyList<McpOperation> Operations,
    IReadOnlyList<string> Notes,
    string Instructions);

/// <summary>A machine the operator works from, the assistants installed there and the servers wanted on it.</summary>
public sealed record WorkstationLine(string Id, string Name, string Os, string? Environment, IReadOnlyList<string> Clients, IReadOnlyList<WantedLine> Wanted, string? Notes);

/// <summary>A server wanted on a workstation, in which clients and at which scope.</summary>
public sealed record WantedLine(string Server, IReadOnlyList<string> Clients, string? Scope);

/// <summary>One step of a plan: a server in a client, to install or to remove if present.</summary>
public sealed record PlanItem(string Server, string Client, string ClientName, string Scope, IReadOnlyList<string> RequiresEnv, IReadOnlyList<string> RequiresSecrets, IReadOnlyList<McpOperation> Operations, IReadOnlyList<string> Notes);

/// <summary>What to install, what to remove if present, and what the machine must provide, to bring a workstation in line with the catalog.</summary>
public sealed record WorkstationPlanAnswer(string Workstation, string Os, IReadOnlyList<PlanItem> Install, IReadOnlyList<PlanItem> Remove, IReadOnlyList<string> RequiresEnv, IReadOnlyList<string> RequiresSecrets, string Instructions);

/// <summary>
/// The tools about the operator's MCP servers (D-110): which they are and how to install one for the client of the assistant that
/// asks, on its machine. Read-only: Portarr describes, the assistant installs, with the operator's approval. Both need <c>mcp.use</c>.
/// </summary>
public sealed class McpServerTools
{
    public const string InstallAction = "install";
    public const string RemoveAction = "remove";

    public const string Instructions =
        "Show the operator exactly what you will run or write, wait for their approval, and change nothing else in the file. " +
        "These operations are for the client you are and the operating system you run on. A variable listed in requiresEnv must " +
        "exist on this machine before the client starts: say so if it does not, and never write a secret value yourself. The operation of kind " +
        "provision reads the secrets listed in requiresSecrets from the operator's vault, in this machine's own shell: run it as given (after approval), " +
        "never fetch, print or write a secret value yourself.";

    private readonly IMcpServerService _servers;
    private readonly IMcpWorkstationService _workstations;
    private readonly IMcpRenderingService _rendering;

    public McpServerTools(IMcpServerService servers, IMcpWorkstationService workstations, IMcpRenderingService rendering)
    {
        _servers = servers;
        _workstations = workstations;
        _rendering = rendering;
    }

    [RequiresPermissions(Permissions.McpUse)]
    [McpServerTool(Name = "list_mcp_servers", Title = "List MCP servers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("The MCP servers the operator uses: remote or local, their default scope, and for a remote one whether it is up (status healthy, authentication-required, down, unknown, waiting) and since when it has been up or down, with the time it was last seen healthy. A local server or one with check: false is not-checked. Call get_mcp_server to install one.")]
    public IReadOnlyList<McpServerLine> ListMcpServers()
    {
        return _servers.List().Select(Line).ToList();
    }

    [RequiresPermissions(Permissions.McpUse)]
    [McpServerTool(Name = "get_mcp_server", Title = "Install or remove an MCP server", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("How to install one of the operator's MCP servers in a client on this machine: the command to run and/or the entry to merge into the client's configuration file, the variables the machine must provide, and notes. Or, with action remove, how to remove it if present. Nothing is changed by this call: show the operator what you will do and wait for their approval.")]
    public McpInstallAnswer GetMcpServer(
        [Description("The server id or name from list_mcp_servers.")] string server,
        [Description("The client you are: claude-code, claude-desktop, cursor, vscode, windsurf, gemini-cli, codex-cli, opencode or zed.")] string client,
        [Description("user, project or local; omit for the server's default scope.")] string? scope = null,
        [Description("The operating system of this machine: windows, linux or macos; omit for linux.")] string? os = null,
        [Description("install (default) or remove.")] string? action = null)
    {
        var view = _servers.Find(server)
            ?? throw new McpException($"No MCP server '{server}'. Servers: {string.Join(", ", _servers.List().Select(s => s.Server.Id))}.");

        var removal = string.Equals(action?.Trim(), RemoveAction, StringComparison.OrdinalIgnoreCase);
        if (action is { Length: > 0 } && !removal && !string.Equals(action.Trim(), InstallAction, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"Unknown action '{action}'. Actions: {InstallAction}, {RemoveAction}.");
        }

        McpRendering rendering;
        try
        {
            rendering = removal ? _rendering.Remove(view.Server, client, scope, os) : _rendering.Install(view.Server, client, scope, os);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }

        var system = _rendering.NormalizeOs(os);
        return new McpInstallAnswer(
            rendering.ServerId,
            rendering.Client,
            rendering.ClientName,
            rendering.Scope,
            system,
            removal ? RemoveAction : InstallAction,
            rendering.Supported,
            rendering.RequiresEnv,
            rendering.RequiresSecrets,
            rendering.Operations,
            rendering.Notes,
            Instructions);
    }

    [RequiresPermissions(Permissions.McpUse)]
    [McpServerTool(Name = "list_workstations", Title = "List workstations", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("The machines the operator works from with an AI assistant (a laptop, a training PC, the dev VM): their operating system, the assistants installed there and the MCP servers wanted on each. Call get_workstation_plan to bring one in line.")]
    public IReadOnlyList<WorkstationLine> ListWorkstations()
    {
        return _workstations.List().Select(v => WorkstationOf(v.Workstation)).ToList();
    }

    [RequiresPermissions(Permissions.McpUse)]
    [McpServerTool(Name = "get_workstation_plan", Title = "Plan of a workstation", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("What to do on a workstation to match the catalog: for each wanted server and client the command and/or file entry to install, then what to remove if present (servers of the catalog not wanted there), and the variables the machine must provide. Nothing is changed by this call and nothing records that you applied it: show the operator each step and wait for their approval.")]
    public WorkstationPlanAnswer GetWorkstationPlan(
        [Description("The workstation id or name from list_workstations: the machine you are on.")] string workstation,
        [Description("windows, linux or macos; omit for the workstation's own system.")] string? os = null)
    {
        var view = _workstations.Find(workstation)
            ?? throw new McpException($"No workstation '{workstation}'. Workstations: {string.Join(", ", _workstations.List().Select(w => w.Workstation.Id))}.");

        McpWorkstationPlan plan;
        try
        {
            plan = _workstations.GetPlan(view.Workstation, os);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }

        return new WorkstationPlanAnswer(plan.Workstation.Id, plan.Os, plan.Install.Select(Item).ToList(), plan.Remove.Select(Item).ToList(), plan.RequiresEnv, plan.RequiresSecrets, Instructions);
    }

    private static WorkstationLine WorkstationOf(WorkstationDefinition workstation)
    {
        return new WorkstationLine(
            workstation.Id,
            workstation.Name,
            workstation.Os,
            workstation.EnvironmentId,
            workstation.Clients,
            workstation.Wanted.Select(w => new WantedLine(w.ServerId, w.Clients, w.Scope)).ToList(),
            workstation.Notes);
    }

    private static PlanItem Item(McpRendering rendering)
    {
        return new PlanItem(rendering.ServerId, rendering.Client, rendering.ClientName, rendering.Scope, rendering.RequiresEnv, rendering.RequiresSecrets, rendering.Operations, rendering.Notes);
    }

    private static McpServerLine Line(McpServerView view)
    {
        var server = view.Server;
        var health = view.Health;
        return new McpServerLine(
            server.Id,
            server.Name,
            server.Transport,
            server.Scope,
            server.Http?.Url.OriginalString,
            server.Stdio is { } stdio ? string.Join(' ', new[] { stdio.Command }.Concat(stdio.Args)) : null,
            view.Status,
            Time(health?.Since),
            Time(health?.LastHealthyAt),
            Time(health?.LastCheckedAt),
            health is { CheckFailed: true } ? CheckErrors.CheckFailed : health?.Error,
            server.AppId,
            server.Notes);
    }

    private static string? Time(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);
}
