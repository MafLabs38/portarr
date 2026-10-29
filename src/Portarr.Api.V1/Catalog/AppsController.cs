using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Status;

namespace Portarr.Api.V1.Catalog;

[ApiController]
[Route("api/v1/apps")]
public sealed class AppsController : ControllerBase
{
    private readonly IFleetService _fleetService;

    public AppsController(IFleetService fleetService)
    {
        _fleetService = fleetService;
    }

    [HttpGet]
    public IReadOnlyList<AppResource> GetApps()
    {
        return _fleetService.GetApps().Select(app => app.ToResource()).ToList();
    }

    [HttpGet("{id}")]
    public ActionResult<AppResource> GetApp(string id)
    {
        var app = _fleetService.GetApps().FirstOrDefault(a => a.App.Id == id);
        return app is null ? NotFound() : app.ToResource();
    }
}
