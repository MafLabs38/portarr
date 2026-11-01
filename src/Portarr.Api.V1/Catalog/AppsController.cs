using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Core.Status;

namespace Portarr.Api.V1.Catalog;

[ApiController]
[Authorize(Policy = Permissions.AppsView)]
[Route("api/v1/apps")]
public sealed class AppsController : ControllerBase
{
    private readonly IFleetService _fleetService;

    public AppsController(IFleetService fleetService)
    {
        _fleetService = fleetService;
    }

    /// <summary>The apps of the catalog with their status and URLs.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AppResource>>(StatusCodes.Status200OK)]
    public IReadOnlyList<AppResource> GetApps()
    {
        return _fleetService.GetApps().Select(app => app.ToResource()).ToList();
    }

    /// <summary>One app by its catalog id.</summary>
    /// <response code="404">No such app.</response>
    [HttpGet("{id}")]
    [ProducesResponseType<AppResource>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AppResource> GetApp(string id)
    {
        var app = _fleetService.GetApps().FirstOrDefault(a => a.App.Id == id);
        return app is null ? NotFound() : app.ToResource();
    }
}
