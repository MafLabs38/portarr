using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Core.Mcp.Clients;

namespace Portarr.Api.V1.Mcp;

/// <summary>The AI clients Portarr can render a server for, whatever library registered them (D-113).</summary>
[ApiController]
[Authorize(Policy = Permissions.McpUse)]
[Route("api/v1/mcp/clients")]
[Produces("application/json")]
public sealed class McpClientsController : ControllerBase
{
    private readonly IMcpClientRegistry _clients;

    public McpClientsController(IMcpClientRegistry clients)
    {
        _clients = clients;
    }

    /// <summary>The clients a server can be rendered for, and the scopes each holds servers at.</summary>
    /// <response code="200">The clients.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<McpClientResource>>(StatusCodes.Status200OK)]
    public IReadOnlyList<McpClientResource> List()
    {
        return _clients.All.Select(c => new McpClientResource(c.Id, c.Name, c.Scopes)).ToList();
    }
}
