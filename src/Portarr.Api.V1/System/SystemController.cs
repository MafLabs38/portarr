using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Core.SystemInfo;

namespace Portarr.Api.V1.System;

[ApiController]
[Authorize(Policy = Permissions.SystemView)]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    private readonly ISystemStatusService _systemStatusService;

    public SystemController(ISystemStatusService systemStatusService)
    {
        _systemStatusService = systemStatusService;
    }

    /// <summary>Version, runtime, database engine, data folder and uptime of this Portarr.</summary>
    [HttpGet("status")]
    [ProducesResponseType<SystemStatusResource>(StatusCodes.Status200OK)]
    public async Task<SystemStatusResource> GetStatusAsync(CancellationToken cancellationToken)
    {
        var status = await _systemStatusService.GetStatusAsync(cancellationToken);
        return status.ToResource();
    }
}
