using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Core.Mcp;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Api.V1.Mcp;

/// <summary>
/// The operator's MCP servers: what the catalog declares, whether each remote one is up and since when, and how to install
/// each in every client. Read-only: the definitions are edited in the catalog file (D-112), and nothing here reaches a workstation (D-110).
/// </summary>
[ApiController]
[Authorize(Policy = Permissions.McpUse)]
[Route("api/v1/mcp")]
[Produces("application/json")]
public sealed class McpController : ControllerBase
{
    private readonly IMcpServerService _servers;
    private readonly IMcpWorkstationService _workstations;

    public McpController(IMcpServerService servers, IMcpWorkstationService workstations)
    {
        _servers = servers;
        _workstations = workstations;
    }

    /// <summary>The MCP servers of the catalog, in its order, with their health.</summary>
    /// <response code="200">The servers; empty when the catalog declares none.</response>
    [HttpGet("servers")]
    [ProducesResponseType<IReadOnlyList<McpServerResource>>(StatusCodes.Status200OK)]
    public IReadOnlyList<McpServerResource> List()
    {
        return _servers.List().Select(s => s.ToResource()).ToList();
    }

    /// <summary>One server and how to install it in every client, for the operating system asked (Linux by default).</summary>
    /// <param name="id">The server id.</param>
    /// <param name="os">windows, linux or macos: paths and commands differ.</param>
    /// <response code="200">The server and a rendering per client, each at the server's default scope when the client has it, else at its first scope.</response>
    /// <response code="400">An unknown operating system.</response>
    /// <response code="404">No such server.</response>
    [HttpGet("servers/{id}")]
    [ProducesResponseType<McpServerDetailResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<McpServerDetailResource> Get(string id, [FromQuery] string? os = null)
    {
        if (_servers.Find(id) is not { } view)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "No such MCP server", detail: $"The catalog declares no MCP server '{id}'.");
        }

        try
        {
            var renderings = McpClients.All
                .Select(client => _servers.Render(view.Server, client.Id, client.Scopes.Contains(view.Server.Scope) ? view.Server.Scope : client.Scopes[0], os).ToResource())
                .ToList();
            return new McpServerDetailResource(view.ToResource(), string.IsNullOrWhiteSpace(os) ? McpServerService.DefaultOs : os.Trim().ToLowerInvariant(), renderings);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: exception.Message);
        }
    }

    /// <summary>The workstations the catalog declares, with the servers wanted on each.</summary>
    /// <response code="200">The workstations; empty when the catalog declares none.</response>
    [HttpGet("workstations")]
    [ProducesResponseType<IReadOnlyList<McpWorkstationResource>>(StatusCodes.Status200OK)]
    public IReadOnlyList<McpWorkstationResource> ListWorkstations()
    {
        return _workstations.List().Select(w => w.Workstation.ToResource()).ToList();
    }

    /// <summary>The plan of one workstation: what to install in each client, what to remove if present, and the variables the machine must provide.</summary>
    /// <param name="id">The workstation id.</param>
    /// <param name="os">windows, linux or macos; the workstation's own system by default.</param>
    /// <response code="200">The plan. Nothing is tracked about whether it was applied.</response>
    /// <response code="400">An unknown operating system.</response>
    /// <response code="404">No such workstation.</response>
    [HttpGet("workstations/{id}")]
    [ProducesResponseType<McpWorkstationPlanResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<McpWorkstationPlanResource> GetWorkstation(string id, [FromQuery] string? os = null)
    {
        if (_workstations.Find(id) is not { } view)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "No such workstation", detail: $"The catalog declares no workstation '{id}'.");
        }

        try
        {
            return _workstations.GetPlan(view.Workstation, os).ToResource();
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: exception.Message);
        }
    }

    /// <summary>The clients a server can be rendered for, and the scopes each holds servers at.</summary>
    /// <response code="200">The clients.</response>
    [HttpGet("clients")]
    [ProducesResponseType<IReadOnlyList<McpClientResource>>(StatusCodes.Status200OK)]
    public IReadOnlyList<McpClientResource> Clients()
    {
        return McpClients.All.Select(c => new McpClientResource(c.Id, c.Name, c.Scopes)).ToList();
    }
}
