using Microsoft.AspNetCore.Mvc;
using Portarr.Core.SystemInfo;

namespace Portarr.Api.V1.System;

[ApiController]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    private readonly ISystemStatusService _systemStatusService;

    public SystemController(ISystemStatusService systemStatusService)
    {
        _systemStatusService = systemStatusService;
    }

    [HttpGet("status")]
    public SystemStatusResource GetStatus()
    {
        return _systemStatusService.GetStatus().ToResource();
    }
}
