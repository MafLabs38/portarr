using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Accounts;
using Portarr.Core.Catalog;
using Portarr.Core.Catalog.Editing;

namespace Portarr.Api.V1.Catalog;

[ApiController]
[Authorize(Policy = Permissions.AppsView)]
[Route("api/v1/catalog")]
public sealed class CatalogStatusController : ControllerBase
{
    private readonly ICatalogService _catalogService;
    private readonly ICatalogEditor _editor;

    public CatalogStatusController(ICatalogService catalogService, ICatalogEditor editor)
    {
        _catalogService = catalogService;
        _editor = editor;
    }

    /// <summary>Whether the catalog file loaded, its errors, its version and whether it can be edited through the API.</summary>
    [HttpGet("status")]
    [ProducesResponseType<CatalogStatusResource>(StatusCodes.Status200OK)]
    public CatalogStatusResource GetStatus()
    {
        return _catalogService.Current.ToResource(_editor.Writable);
    }
}
