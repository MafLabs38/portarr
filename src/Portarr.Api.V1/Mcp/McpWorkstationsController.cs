using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Core.Mcp;

namespace Portarr.Api.V1.Mcp;

/// <summary>
/// The workstations the catalog declares, the servers wanted on each, and the plan that brings one in line with the catalog. Read-only:
/// Portarr describes, the assistant on the machine applies (D-110), and nothing is tracked about whether it was.
/// </summary>
[ApiController]
[Authorize(Policy = Permissions.McpUse)]
[Route("api/v1/mcp/workstations")]
[Produces("application/json")]
public sealed class McpWorkstationsController : ControllerBase
{
    private readonly IMcpWorkstationService _workstations;

    public McpWorkstationsController(IMcpWorkstationService workstations)
    {
        _workstations = workstations;
    }

    /// <summary>The workstations the catalog declares, with the servers wanted on each.</summary>
    /// <response code="200">The workstations; empty when the catalog declares none.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<McpWorkstationResource>>(StatusCodes.Status200OK)]
    public IReadOnlyList<McpWorkstationResource> List()
    {
        return _workstations.List().Select(w => w.Workstation.ToResource()).ToList();
    }

    /// <summary>The plan of one workstation: what to install in each client, what to remove if present, and the variables the machine must provide.</summary>
    /// <param name="id">The workstation id.</param>
    /// <param name="os">windows, linux or macos; the workstation's own system by default.</param>
    /// <response code="200">The plan. Nothing is tracked about whether it was applied.</response>
    /// <response code="400">An unknown operating system.</response>
    /// <response code="404">No such workstation.</response>
    [HttpGet("{id}")]
    [ProducesResponseType<McpWorkstationPlanResource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<McpWorkstationPlanResource> Get(string id, [FromQuery] string? os = null)
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
}
