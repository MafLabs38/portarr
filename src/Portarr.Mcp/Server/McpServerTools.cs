using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog;
using Portarr.Core.Checks;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Capture;
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

/// <summary>A header or an environment entry of a server found in a client's configuration: its name and a variable or a harmless literal, never a secret value.</summary>
public sealed record CaptureValueInput(
    [property: Description("The name of the header or of the environment variable the server gets.")] string Name,
    [property: Description("The NAME of the environment variable that holds the value on this machine. For a secret you saw written in the file, propose a name (for example the server's id in capitals plus the header); never send the secret.")] string? Variable = null,
    [property: Description("A harmless text (a media type, a log level). Never a key, a token, a password: under a name that suggests a credential it is refused.")] string? Literal = null);

/// <summary>An MCP server found in a client's configuration on this machine.</summary>
public sealed record CaptureServerInput(
    [property: Description("The name the client gives the server.")] string Name,
    [property: Description("http for a remote server, stdio for a local process.")] string Transport,
    [property: Description("A remote server's address, without credentials.")] string? Url = null,
    [property: Description("A local server's program (npx, uvx, a path), never a shell line.")] string? Command = null,
    [property: Description("A local server's arguments, one per entry.")] IReadOnlyList<string>? Args = null,
    [property: Description("A remote server's headers.")] IReadOnlyList<CaptureValueInput>? Headers = null,
    [property: Description("A local server's environment.")] IReadOnlyList<CaptureValueInput>? Env = null);

/// <summary>What happened to a capture: it waits in the inbox for the operator.</summary>
public sealed record CaptureAnswer(int CaptureId, int Servers, int Replaced, string Message);

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
    private readonly IMcpCaptureService _captures;
    private readonly IHttpContextAccessor _httpContext;

    public McpServerTools(IMcpServerService servers, IMcpWorkstationService workstations, IMcpCaptureService captures, IHttpContextAccessor httpContext)
    {
        _servers = servers;
        _workstations = workstations;
        _captures = captures;
        _httpContext = httpContext;
    }

    [RequiresPermissions(Permissions.McpUse)]
    [McpServerTool(Name = "list_mcp_servers", Title = "List MCP servers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("The MCP servers the operator uses: remote or local, their default scope, and for a remote one whether it is up (status healthy, authentication-required, down, unknown, waiting) and since when it has been up or down, with the time it was last seen healthy. A local server or one with check: false is not-checked. Call get_mcp_server to install one.")]
    public IReadOnlyList<McpServerLine> ListMcpServers()
    {
        return _servers.List().Select(Line).ToList();
    }

    [RequiresPermissions(Permissions.McpUse)]
    [McpServerTool(Name = "get_mcp_server", Title = "Install an MCP server", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
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
            rendering = removal ? _servers.RenderRemoval(view.Server, client, scope, os) : _servers.Render(view.Server, client, scope, os);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }

        var system = string.IsNullOrWhiteSpace(os) ? McpServerService.DefaultOs : os.Trim().ToLowerInvariant();
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

    [RequiresPermissions(Permissions.McpUse)]
    [McpServerTool(Name = "submit_mcp_capture", Title = "Hand over the MCP configuration of this machine", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Hands over the MCP servers configured in one client on this machine, so that the operator can review them and add them to Portarr's catalog. Writes only Portarr's inbox: nothing reaches the catalog until the operator accepts it in the MCP page. Read the client's configuration file yourself and describe each server: its name, address or command and arguments, and the NAMES of its headers or variables. NEVER send a secret value: a key, a token or a password is refused, and the whole capture with it; for a value written in the file, send the name of the environment variable that should hold it. Tell the operator what you are about to send and wait for their approval.")]
    public async Task<CaptureAnswer> SubmitMcpCapture(
        [Description("The id of this machine from list_workstations, or the one the operator gives (lowercase letters, digits, dashes).")] string workstation,
        [Description("The client whose configuration you read: claude-code, claude-desktop, cursor, vscode, windsurf, gemini-cli, codex-cli, opencode or zed.")] string client,
        [Description("The scope you read: user, project or local.")] string scope,
        [Description("Every MCP server of that configuration, at most 50.")] IReadOnlyList<CaptureServerInput> servers,
        CancellationToken cancellationToken = default)
    {
        var request = new CaptureRequest(
            workstation?.Trim().ToLowerInvariant() ?? string.Empty,
            client?.Trim().ToLowerInvariant() ?? string.Empty,
            scope?.Trim().ToLowerInvariant() ?? string.Empty,
            (servers ?? []).Select(Captured).ToList());
        try
        {
            var receipt = await _captures.SubmitAsync(request, _httpContext.HttpContext?.User.Identity?.Name, cancellationToken);
            return new CaptureAnswer(receipt.CaptureId, receipt.Servers, receipt.Replaced, "Handed over: the operator reviews it in the MCP page (Captured tab). Nothing was added to the catalog.");
        }
        catch (McpCaptureException exception)
        {
            throw new McpException("Refused, nothing was stored: " + string.Join(" ", exception.Errors));
        }
    }

    private static CapturedServer Captured(CaptureServerInput input)
    {
        return new CapturedServer(
            input.Name,
            input.Transport?.Trim().ToLowerInvariant() ?? string.Empty,
            input.Url,
            input.Command,
            input.Args ?? [],
            (input.Headers ?? []).Select(v => new CapturedValue(v.Name, v.Variable, v.Literal)).ToList(),
            (input.Env ?? []).Select(v => new CapturedValue(v.Name, v.Variable, v.Literal)).ToList());
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
