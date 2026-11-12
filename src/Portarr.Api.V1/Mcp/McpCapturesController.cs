using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Core.Mcp.Capture;
using Portarr.Http.Authentication;

namespace Portarr.Api.V1.Mcp;

/// <summary>
/// The inbox of captured MCP servers (V9, D-116): what the assistants handed over from the workstations, compared with the catalog. Reading it
/// needs <c>mcp.use</c>. Deciding needs a **person**: a browser session that holds <c>catalog.write</c>, never an API key, the MCP tools or the
/// chat (D-112), because accepting writes a definition that becomes a command or a credentialed address on the operator's machines.
/// </summary>
[ApiController]
[Authorize(Policy = Permissions.McpUse)]
[Route("api/v1/mcp/captures")]
[Produces("application/json")]
public sealed class McpCapturesController : ControllerBase
{
    private readonly IMcpCaptureService _captures;

    public McpCapturesController(IMcpCaptureService captures)
    {
        _captures = captures;
    }

    /// <summary>The captured servers waiting for a decision, grouped by identity, each compared with the catalog.</summary>
    /// <response code="200">The groups, oldest first; empty when nothing waits.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<McpCaptureGroup>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<McpCaptureGroup>> List(CancellationToken cancellationToken)
    {
        return _captures.ListAsync(cancellationToken);
    }

    /// <summary>Accepts a group: writes the definition and the wanted lists of the workstations, through the editor (versioned, backed up, audited).</summary>
    /// <remarks>
    /// <c>mode</c> is <c>new</c> (add a definition), <c>replace</c> (replace the server it matches, keeping its notes, scope, check and app) or
    /// <c>existing</c> (the catalog has it: only the wanted lists). A catalog that is read-only answers 200 with <c>readOnly: true</c> and the
    /// YAML to paste into the file; the group stays in the inbox.
    /// </remarks>
    /// <param name="key">The group's key, from the list.</param>
    /// <param name="request">What the person decided.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <response code="200">What was written, or the YAML to paste when the catalog cannot be written.</response>
    /// <response code="400">The request or the resulting catalog is not valid, or the group is no longer in the inbox.</response>
    [HttpPost("{key}/accept")]
    [Authorize(Policy = Permissions.CatalogWrite)]
    [Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
    [ProducesResponseType<AcceptResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AcceptResult>> Accept(string key, AcceptRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _captures.AcceptAsync(key, request, User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
            return result.Errors.Count > 0 && !result.Written && !result.ReadOnly
                ? Problem(statusCode: StatusCodes.Status400BadRequest, title: "The catalog refused the definition", detail: string.Join(" ", result.Errors))
                : result;
        }
        catch (McpCaptureException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: exception.Message);
        }
    }

    /// <summary>Dismisses a group: the servers leave the inbox, nothing is written to the catalog.</summary>
    /// <param name="key">The group's key, from the list.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <response code="200">How many captured servers were dismissed.</response>
    /// <response code="400">The group is no longer in the inbox.</response>
    [HttpPost("{key}/dismiss")]
    [Authorize(Policy = AuthenticationServiceCollectionExtensions.SessionPolicyName)]
    [ProducesResponseType<DismissResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DismissResult>> Dismiss(string key, CancellationToken cancellationToken)
    {
        try
        {
            return new DismissResult(await _captures.DismissAsync(key, User.Identity?.Name, cancellationToken));
        }
        catch (McpCaptureException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: exception.Message);
        }
    }
}

/// <summary>How many captured servers a dismissal removed from the inbox.</summary>
public sealed record DismissResult(int Dismissed);
