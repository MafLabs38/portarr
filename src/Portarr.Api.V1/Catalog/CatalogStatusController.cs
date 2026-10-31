using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Catalog;

namespace Portarr.Api.V1.Catalog;

[ApiController]
[Route("api/v1/catalog")]
public sealed class CatalogStatusController : ControllerBase
{
    private readonly ICatalogService _catalogService;

    public CatalogStatusController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    /// <summary>Whether the catalog file loaded, and its errors.</summary>
    [HttpGet("status")]
    [ProducesResponseType<CatalogStatusResource>(StatusCodes.Status200OK)]
    public CatalogStatusResource GetStatus()
    {
        return _catalogService.Current.ToResource();
    }
}
