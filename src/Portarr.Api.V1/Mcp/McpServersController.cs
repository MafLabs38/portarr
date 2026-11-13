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
public sealed class McpServersController : ControllerBase
{
    private readonly IMcpServerService _servers;
    private readonly IMcpRenderingService _rendering;

    public McpServersController(IMcpServerService servers, IMcpRenderingService rendering)
    {
        _servers = servers;
        _rendering = rendering;
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
            var renderings = _rendering.InstallEverywhere(view.Server, os).Select(r => r.ToResource()).ToList();
            return new McpServerDetailResource(view.ToResource(), _rendering.NormalizeOs(os), renderings);
        }
        catch (ArgumentException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: exception.Message);
        }
    }
}
