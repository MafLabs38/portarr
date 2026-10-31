using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Portarr.Core.Catalog;
using Portarr.Core.Data;

namespace Portarr.Api.V1.Data;

[ApiController]
[Route("api/v1/data")]
public sealed class DataController : ControllerBase
{
    private readonly IDataService _dataService;
    private readonly ICatalogService _catalogService;

    public DataController(IDataService dataService, ICatalogService catalogService)
    {
        _dataService = dataService;
        _catalogService = catalogService;
    }

    /// <summary>The data the catalog follows, with the state of their local and off-site copies (at risk first), and the backup tools read.</summary>
    /// <remarks>D-77 to D-82. Built from memory (last backup reads): this call reaches no connector.</remarks>
    [HttpGet]
    [ProducesResponseType<DataResource>(StatusCodes.Status200OK)]
    public DataResource Get()
    {
        return _dataService.GetReport().ToResource(_catalogService.Current.Catalog.Environments);
    }
}
