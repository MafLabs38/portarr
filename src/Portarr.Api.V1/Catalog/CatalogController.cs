using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Catalog;
using Portarr.Core.Status;

namespace Portarr.Api.V1.Catalog;

[ApiController]
[Route("api/v1")]
public sealed class CatalogController : ControllerBase
{
    private readonly ICatalogService _catalogService;
    private readonly IFleetService _fleetService;

    public CatalogController(ICatalogService catalogService, IFleetService fleetService)
    {
        _catalogService = catalogService;
        _fleetService = fleetService;
    }

    [HttpGet("catalog/status")]
    public CatalogStatusResource GetStatus()
    {
        return _catalogService.Current.ToResource();
    }

    [HttpGet("apps")]
    public IReadOnlyList<AppResource> GetApps()
    {
        return _fleetService.GetApps().Select(app => app.ToResource()).ToList();
    }

    [HttpGet("apps/{id}")]
    public ActionResult<AppResource> GetApp(string id)
    {
        var app = _fleetService.GetApps().FirstOrDefault(a => a.App.Id == id);
        return app is null ? NotFound() : app.ToResource();
    }

    [HttpGet("stacks")]
    public IReadOnlyList<StackResource> GetStacks()
    {
        return _fleetService.GetStacks().Select(stack => stack.ToResource()).ToList();
    }
}
